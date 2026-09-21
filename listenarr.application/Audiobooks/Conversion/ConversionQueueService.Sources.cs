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
namespace Listenarr.Application.Audiobooks.Conversion
{
    /// <summary>
    /// What of a book's files a conversion can and cannot use.
    /// </summary>
    public sealed partial class ConversionQueueService
    {
        /// <summary>
        /// The files an encode would fail on, as one sentence naming them, or null when
        /// every source is usable. A file the scan probed and could not read carries a
        /// duration of zero - one never probed carries none, and is given the benefit of
        /// the doubt; one the scan could not find is flagged as such.
        /// </summary>
        internal static string? DescribeUnreadableFiles(Audiobook audiobook)
        {
            var files = audiobook.Files;
            if (files == null || files.Count == 0)
            {
                return null;
            }

            var missing = files
                .Where(file => file.IsNotFound)
                .Select(file => Path.GetFileName(file.Path))
                .ToList();
            var unreadable = files
                .Where(file => !file.IsNotFound && file.DurationSeconds is <= 0)
                .Select(file => Path.GetFileName(file.Path))
                .ToList();
            if (missing.Count == 0 && unreadable.Count == 0)
            {
                return null;
            }

            var parts = new List<string>();
            if (unreadable.Count > 0)
            {
                parts.Add($"{Plural(unreadable.Count, "file")} could not be read by ffprobe ({Join(unreadable)})");
            }

            if (missing.Count > 0)
            {
                parts.Add($"{Plural(missing.Count, "file")} {(missing.Count == 1 ? "is" : "are")} not where the record says ({Join(missing)})");
            }

            return $"{string.Join(", and ", parts)}. Replace or remove {(unreadable.Count + missing.Count == 1 ? "it" : "them")}, rescan, then convert.";

            static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
            static string Join(List<string> names) =>
                names.Count <= 3 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(3))}, and {names.Count - 3} more";
        }

        private static int CountConvertibleFiles(Audiobook audiobook)
        {
            var files = audiobook.Files;
            if (files == null || files.Count == 0)
            {
                return 0;
            }

            return files.Count(file =>
                !string.IsNullOrWhiteSpace(file.Path)
                && string.Equals(
                    Path.GetExtension(file.Path),
                    ".mp3",
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}
