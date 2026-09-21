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
using Listenarr.Domain.Audiobooks.Conversion;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Chapters
{
    /// <summary>The edition's chapter list, fetched from Audnexus at a pace a shared service can bear.</summary>
    public sealed partial class ChapterRepairService
    {
        /// <summary>Audnexus is a shared service; a library-wide planning pass must not hammer it.</summary>
        public static readonly TimeSpan AudnexusMinimumGap = TimeSpan.FromSeconds(1.5);

        private static readonly SemaphoreSlim AudnexusGate = new(1, 1);
        private static DateTime _lastAudnexusCallUtc = DateTime.MinValue;

        /// <summary>The edition's chapters; <c>Unavailable</c> when Audnexus could not be asked, as against having none.</summary>
        private async Task<((IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime)? Edition, bool Unavailable)> FetchAudnexusAsync(
            string asin,
            CancellationToken cancellationToken)
        {
            if (audnexus == null)
            {
                return default;
            }

            try
            {
                await AudnexusGate.WaitAsync(cancellationToken);
                try
                {
                    var wait = _lastAudnexusCallUtc + AudnexusMinimumGap - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, cancellationToken);
                    }

                    _lastAudnexusCallUtc = DateTime.UtcNow;
                }
                finally
                {
                    AudnexusGate.Release();
                }

                var lookup = await audnexus.LookupChaptersAsync(asin, cancellationToken: cancellationToken);
                if (lookup.Unavailable)
                {
                    return (null, true);
                }

                if (lookup.Response?.Chapters is not { Count: > 0 } chapters || lookup.Response.RuntimeLengthMs is not { } runtimeMs)
                {
                    return default;
                }

                var list = new List<EmbeddedChapter>(chapters.Count);
                foreach (var chapter in chapters)
                {
                    if (chapter.StartOffsetMs is not { } startMs)
                    {
                        continue;
                    }

                    var start = TimeSpan.FromMilliseconds(startMs);
                    var end = chapter.LengthMs is { } lengthMs ? start + TimeSpan.FromMilliseconds(lengthMs) : start;
                    list.Add(new EmbeddedChapter(chapter.Title, start, end));
                }

                return ((list, TimeSpan.FromMilliseconds(runtimeMs)), false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Audnexus chapters unavailable for {Asin}", asin);
                return (null, true);
            }
        }
    }
}
