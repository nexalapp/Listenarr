/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Conversion
{
    public sealed partial class ConversionJobProcessor
    {
        /// <summary>
        /// Put back the originals a repair archived ahead of a publication that then failed.
        ///
        /// <para>
        /// A book stored as one M4B has its repair published at the original's own path,
        /// so the original is archived first. When the publication then fails, the book has
        /// no audio in the library at all — Valis sat empty until a hand-made copy, because
        /// a retry refuses a book with nothing left to repair. Moving the original back
        /// (archiving was a plain move, so this is its exact inverse) and rescanning
        /// re-registers it; the book is then as it was before the repair, and a retry
        /// repairs it again from its kept chunks.
        /// </para>
        /// </summary>
        /// <returns>The failure message, saying where the original now is.</returns>
        internal async Task<string> RestoreArchivedOriginalsAsync(
            Audiobook audiobook,
            IReadOnlyList<(string From, string To)> archived,
            string error,
            IServiceProvider services)
        {
            if (archived.Count == 0)
            {
                return error;
            }

            var restored = 0;
            foreach (var (from, to) in archived.Reverse())
            {
                try
                {
                    if (!File.Exists(from) && File.Exists(to))
                    {
                        File.Move(to, from);
                        restored++;
                    }
                }
                catch (Exception ex) when (IsNonFatal(ex))
                {
                    logger.LogError(
                        ex,
                        "Could not put the archived original {Path} back after a failed repair publication",
                        LogRedaction.SanitizeFilePath(to));
                }
            }

            if (restored > 0)
            {
                try
                {
                    // The records were dropped at retirement; a scan registers the files again.
                    await services.GetRequiredService<IScanQueueService>().EnqueueScanAsync(audiobook);
                }
                catch (Exception ex) when (IsNonFatal(ex))
                {
                    logger.LogWarning(ex, "Could not queue a rescan of audiobook {AudiobookId} after restoring its original", audiobook.Id);
                }
            }

            logger.LogWarning(
                "Repair publication failed for audiobook {AudiobookId}; put {Restored} of {Total} archived original(s) back",
                audiobook.Id,
                restored,
                archived.Count);

            return restored == archived.Count
                ? error.Replace(
                    "The original files have been left alone.",
                    "The original was put back in the library; Retry repairs it again from its kept chunks.",
                    StringComparison.Ordinal)
                : error.Replace(
                    "The original files have been left alone.",
                    "The original is in the archive and could not be put back.",
                    StringComparison.Ordinal);
        }
    }
}
