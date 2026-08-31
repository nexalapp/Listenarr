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
    public interface IImageCacheService
    {
        Task<string?> DownloadAndCacheImageAsync(string imageUrl, string identifier);

        /// <summary>
        /// Caches an image supplied as bytes rather than fetched from a URL. Used for cover
        /// art extracted from an audiobook file, which has no URL to download from.
        /// Returns the relative cached path, or null when the bytes are not a usable image.
        /// </summary>
        Task<string?> CacheImageBytesAsync(byte[] imageBytes, string identifier, string? mediaType);
        Task<string?> MoveToLibraryStorageAsync(string identifier, string? imageUrl = null);
        Task<string?> MoveToAuthorLibraryStorageAsync(string identifier, string? imageUrl = null, bool forceRefresh = false);
        Task<string?> MoveToSeriesLibraryStorageAsync(string identifier, string? imageUrl = null, bool forceRefresh = false);
        Task<string?> GetCachedImagePathAsync(string identifier);
        Task ClearTempCacheAsync();
    }
}
