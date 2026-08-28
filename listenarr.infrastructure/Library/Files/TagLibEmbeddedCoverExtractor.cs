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

namespace Listenarr.Infrastructure.Library.Files
{
    public sealed class TagLibEmbeddedCoverExtractor : IEmbeddedCoverExtractor
    {
        // A cover atom larger than this is not cover art; refusing it keeps a malformed
        // or hostile file from being read wholly into memory.
        private const int MaxCoverBytes = 12 * 1024 * 1024;

        private readonly ILogger<TagLibEmbeddedCoverExtractor> _logger;

        public TagLibEmbeddedCoverExtractor(ILogger<TagLibEmbeddedCoverExtractor> logger)
        {
            _logger = logger;
        }

        public EmbeddedCover? TryExtract(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                using var file = TagLib.File.Create(filePath);
                var pictures = file.Tag?.Pictures;
                if (pictures == null || pictures.Length == 0)
                {
                    return null;
                }

                // Prefer the declared front cover; fall back to the first picture, because
                // most audiobook taggers write a single untyped picture rather than marking it.
                var picture =
                    Array.Find(pictures, p => p.Type == TagLib.PictureType.FrontCover)
                    ?? pictures[0];

                var bytes = picture.Data?.Data;
                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                if (bytes.Length > MaxCoverBytes)
                {
                    _logger.LogWarning(
                        "Ignored embedded cover in {File}: {Bytes} bytes exceeds the {Max} byte limit",
                        LogRedaction.SanitizeFilePath(filePath),
                        bytes.Length,
                        MaxCoverBytes);
                    return null;
                }

                var mediaType = string.IsNullOrWhiteSpace(picture.MimeType)
                    ? "image/jpeg"
                    : picture.MimeType;

                return new EmbeddedCover(bytes, mediaType);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(
                    ex,
                    "No embedded cover could be read from {File}",
                    LogRedaction.SanitizeFilePath(filePath));
                return null;
            }
        }
    }
}
