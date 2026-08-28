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
namespace Listenarr.Application.Audiobooks.Contracts
{
    /// <summary>
    /// The cover art an audiobook file carries inside it, as raw bytes plus the media
    /// type needed to store it under the right extension.
    /// </summary>
    public sealed record EmbeddedCover(byte[] Bytes, string MediaType);

    /// <summary>
    /// Reads embedded cover art out of an audio file.
    ///
    /// This deliberately does not go through ffmpeg: the application bundles ffprobe
    /// only, which can report that an attached picture stream exists but cannot extract
    /// it. TagLib is already a dependency for tag writing and reads MP4 cover atoms
    /// directly, so the picture comes out without shipping another binary.
    /// </summary>
    public interface IEmbeddedCoverExtractor
    {
        /// <summary>
        /// Returns the file's front cover, or null when it carries no usable picture.
        /// Never throws for an unreadable or malformed file: a missing cover is a normal
        /// outcome of a manual import, not an error worth failing the import over.
        /// </summary>
        EmbeddedCover? TryExtract(string filePath);
    }
}
