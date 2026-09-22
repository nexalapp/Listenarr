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
using Listenarr.Domain.Common;

namespace Listenarr.Domain.Audiobooks.Conversion
{
    /// <summary>
    /// Which of a book's files a conversion would read.
    ///
    /// <para>
    /// Anything ffmpeg can decode, which is not already the thing being made. The rule
    /// used to be "an MP3", and it was written when every unconverted book in this
    /// library was one; a release of fifty-one numbered .mp4 chapter files is just as
    /// much a book in pieces, and refusing it said only that there was nothing to
    /// convert. An .m4a or a .flac in parts is the same case again.
    /// </para>
    /// <para>
    /// An .m4b is left alone: it is the format conversion produces, so a book already
    /// in it has nothing to gain. Several .m4b parts are a book someone could want
    /// joined, but joining them is a different operation from converting to the format
    /// they are already in, and it is not what this offers.
    /// </para>
    /// </summary>
    public static class ConvertibleSource
    {
        /// <summary>The format a conversion produces, and so the one it never reads.</summary>
        public const string TargetExtension = ".m4b";

        public static bool IsConvertible(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !FileUtils.IsAudioFile(path))
            {
                return false;
            }

            return !string.Equals(Path.GetExtension(path), TargetExtension, StringComparison.OrdinalIgnoreCase);
        }
    }
}
