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
    /// Builds catalogue-shaped metadata from what an audiobook file carries inside it,
    /// for books no online provider can match. The result is the same shape the add-to-
    /// library flow already accepts, so a manual import needs no separate add path.
    /// </summary>
    public interface IEmbeddedFileMetadataService
    {
        /// <summary>
        /// Reads embedded tags and cover art from <paramref name="filePath"/>.
        /// Returns null when the file cannot be read or metadata processing is disabled.
        /// The returned metadata carries no ASIN unless the file itself supplies one.
        /// </summary>
        Task<AudibleBookMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
