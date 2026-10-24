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
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Tagging
{
    public sealed partial class LibraryTagIndexService
    {
        /// <summary>
        /// Roll each book's rows up into the two health facts this table is the authority
        /// on — would a tag write change anything, would a rename — and store them, so the
        /// library list can score a thousand books without a stat per file. Everything
        /// that changes either answer already rebuilds this index: a scan, a tag write, a
        /// rename preview opened after a pattern change.
        /// </summary>
        private async Task PersistBookHealthAsync(
            IReadOnlyList<LibraryTagRow> rows,
            CancellationToken cancellationToken)
        {
            foreach (var book in rows.GroupBy(row => row.AudiobookId))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (tagsClean, tagsReason) = JudgeTags([.. book]);
                var (namingClean, namingReason) = JudgeNaming([.. book]);
                try
                {
                    await audiobookRepository.SetTagNamingHealthAsync(
                        book.Key,
                        new TagNamingHealthRecord(tagsClean, tagsReason, namingClean, namingReason, DateTime.UtcNow),
                        cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The table itself is the product here; its health by-product must
                    // not take it down.
                    logger.LogDebug(exception, "Could not store health facts for audiobook {AudiobookId}", book.Key);
                }
            }
        }

        private static (bool? Clean, string? Reason) JudgeTags(IReadOnlyList<LibraryTagRow> rows)
        {
            // Only the files a tag write could touch: the library's MP3 books cannot carry
            // the M4B tag set and their remedy is conversion, not a write — their format
            // is already its own column. A book of nothing but MP3s has no answer here.
            var writable = rows.Where(row => row.Writable).ToList();
            if (writable.Count == 0)
            {
                return (null, null);
            }

            if (writable.Any(row => row.Error != null))
            {
                return (null, "A file could not be read, so the tags are unverified.");
            }

            var mismatched = writable.Where(row => row.Mismatched.Count > 0).ToList();
            if (mismatched.Count == 0)
            {
                return (true, null);
            }

            var tags = mismatched
                .SelectMany(row => row.Mismatched)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return (false, $"A tag write would change {Join(tags, 4)} in {mismatched.Count} file(s).");
        }

        private static (bool? Clean, string? Reason) JudgeNaming(IReadOnlyList<LibraryTagRow> rows)
        {
            // Null means organizing could not answer for this book — a root offline, a
            // pattern it cannot render — which is not the same as "already correct".
            if (rows.Any(row => row.ExpectedPath == null))
            {
                return (null, null);
            }

            var folderMoved = rows.Any(row => row.PathMismatched);
            var renamed = rows.Count(row => row.FileNameMismatched);
            if (!folderMoved && renamed == 0)
            {
                return (true, null);
            }

            return (false, (folderMoved, renamed) switch
            {
                (true, 0) => "The folder name differs from the naming pattern.",
                (false, _) => $"{renamed} file name(s) differ from the naming pattern.",
                _ => $"The folder name and {renamed} file name(s) differ from the naming pattern."
            });
        }

        private static string Join(IReadOnlyList<string> values, int most) =>
            values.Count <= most
                ? string.Join(", ", values)
                : $"{string.Join(", ", values.Take(most))} and {values.Count - most} more";
    }
}
