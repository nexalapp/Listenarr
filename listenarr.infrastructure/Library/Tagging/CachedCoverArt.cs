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
using Microsoft.Extensions.DependencyInjection;

namespace Listenarr.Infrastructure.Library.Tagging
{
    /// <summary>
    /// The book's cover as Listenarr already holds it, for embedding into a file that
    /// carries none. Shared by tag writes and conversions, so both embed the same image.
    /// </summary>
    internal static class CachedCoverArt
    {
        /// <summary>
        /// A local copy of the cached cover, or null when there is nothing embeddable —
        /// a missing cover is never a reason to fail the write that wanted it.
        /// </summary>
        public static async Task<string?> ResolveAsync(Audiobook audiobook, IServiceProvider services)
        {
            var identifier = audiobook.Asin;
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return null;
            }

            var relative = await services.GetRequiredService<IImageCacheService>()
                .GetCachedImagePathAsync(identifier);
            if (string.IsNullOrWhiteSpace(relative))
            {
                return null;
            }

            var contentRoot = services.GetRequiredService<IApplicationPathService>().ContentRootPath;
            var absolute = Path.GetFullPath(Path.Combine(contentRoot, relative));

            // An SVG placeholder is not cover art, and the mov muxer cannot carry it.
            return IsEmbeddableImage(absolute) && File.Exists(absolute) ? absolute : null;
        }

        /// <summary>The image types the mov muxer can carry as an attached picture.</summary>
        public static bool IsEmbeddableImage(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase);
        }
    }
}
