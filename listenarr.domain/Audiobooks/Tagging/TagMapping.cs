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
    /// What goes into one tag, and whether it may replace what the file already has.
    ///
    /// <para>
    /// <see cref="Pattern"/> is the same template language the file and folder naming
    /// patterns use, deliberately: the album tag has to mirror the folder name, and an
    /// operator who has already written <c>[{Series} {SeriesNumber}] {Title}</c> once
    /// should not have to learn a second syntax to write it again. Empty tokens collapse
    /// the same way, which is what makes one pattern serve both a series book and a
    /// standalone without a conditional.
    /// </para>
    /// </summary>
    public sealed class TagMapping
    {
        /// <summary>
        /// The metadata key as ffmpeg names it — <c>album</c>, <c>description</c>,
        /// <c>SERIES</c>. Keys ffmpeg knows become standard MP4 atoms; the rest become
        /// iTunes freeform atoms, which is how the library's existing files carry
        /// <c>ASIN</c> and <c>SERIES</c>.
        /// </summary>
        public string Tag { get; set; } = string.Empty;

        /// <summary>The naming-pattern template producing the value.</summary>
        public string Pattern { get; set; } = string.Empty;

        public TagWriteMode Mode { get; set; } = TagWriteMode.Never;

        public TagMapping() { }

        public TagMapping(string tag, string pattern, TagWriteMode mode)
        {
            Tag = tag;
            Pattern = pattern;
            Mode = mode;
        }
    }
}
