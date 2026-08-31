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

namespace Listenarr.Application.Audiobooks.Files
{
    public sealed class AudiobookFormatSummary
    {
        public int AudiobookId { get; set; }
        public string? Path { get; set; }
        public string? Format { get; set; }
        public string? Container { get; set; }
        public string? Codec { get; set; }
        public int? Bitrate { get; set; }
    }

    /// <summary>
    /// Derives the container formats of a book from its file summaries.
    /// </summary>
    public static class AudiobookFormats
    {
        /// <summary>
        /// The distinct container formats of one book's files, uppercase and sorted.
        ///
        /// A list rather than one value: a book can legitimately hold more than one —
        /// part-way through a conversion it holds both — and collapsing that would make
        /// it invisible to a filter looking for either.
        ///
        /// Falls back to the container, then to the file extension, when a row carries no
        /// format. Files registered before format extraction ran, or by a path ffprobe
        /// could not read, still have a usable answer sitting on disk.
        /// </summary>
        public static string[] Describe(IReadOnlyList<AudiobookFormatSummary>? files)
        {
            if (files == null || files.Count == 0)
            {
                return [];
            }

            return files
                .Select(file => FirstUsable(file.Format, file.Container, ExtensionOf(file.Path)))
                .Where(format => !string.IsNullOrWhiteSpace(format))
                .Select(format => format!.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        private static string? FirstUsable(params string?[] candidates) =>
            candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

        private static string? ExtensionOf(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var extension = System.IO.Path.GetExtension(path);
            return string.IsNullOrWhiteSpace(extension) ? null : extension.TrimStart('.');
        }
    }
}
