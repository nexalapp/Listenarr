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

namespace Listenarr.Domain.Audiobooks.Tagging
{
    /// <summary>
    /// Which files Listenarr will write tags into.
    ///
    /// <para>
    /// MP4 containers only, and that is the point rather than a limitation. The tag this
    /// whole path exists for is the MP4 <c>desc</c> atom, which is the only place Plex
    /// reads an album summary from; ID3 has no equivalent and cannot be made to have one.
    /// An MP3 book reaches a writable state by being converted first, not by having a
    /// description written into a frame nothing reads.
    /// </para>
    /// </summary>
    public static class TaggableFile
    {
        public static readonly IReadOnlySet<string> Extensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".m4b", ".m4a" };

        public static bool IsTaggable(string? path) =>
            !string.IsNullOrWhiteSpace(path)
            && Extensions.Contains(Path.GetExtension(path));
    }
}
