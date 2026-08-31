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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Catalog
{
    public sealed class EmbeddedFileMetadataService : IEmbeddedFileMetadataService
    {
        private readonly IMetadataService _metadataService;
        private readonly IEmbeddedCoverExtractor _coverExtractor;
        private readonly IImageCacheService _imageCache;
        private readonly ILogger<EmbeddedFileMetadataService> _logger;

        public EmbeddedFileMetadataService(
            IMetadataService metadataService,
            IEmbeddedCoverExtractor coverExtractor,
            IImageCacheService imageCache,
            ILogger<EmbeddedFileMetadataService> logger)
        {
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
            _coverExtractor = coverExtractor ?? throw new ArgumentNullException(nameof(coverExtractor));
            _imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AudibleBookMetadata?> ReadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var audio = await _metadataService.ExtractFileMetadataAsync(filePath);
            if (audio == null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var metadata = new AudibleBookMetadata
            {
                Source = EmbeddedMetadataSource,
                Title = FirstNonEmpty(audio.Title, audio.Album),
                Subtitle = NullIfBlank(audio.Subtitle),
                Description = NullIfBlank(audio.Description),
                Publisher = NullIfBlank(audio.Publisher),
                Language = NullIfBlank(audio.Language),
                Series = NullIfBlank(audio.Series),
                SeriesNumber = FormatSeriesPosition(audio.SeriesPosition),
                PublishYear = audio.Year?.ToString(CultureInfo.InvariantCulture),
                Runtime = audio.Duration > TimeSpan.Zero
                    ? (int)Math.Round(audio.Duration.TotalMinutes, MidpointRounding.AwayFromZero)
                    : null,
                Asin = NullIfBlank(audio.Asin),
            };

            // The album artist is the author of record for an audiobook; artist is the
            // fallback because single-author files often only set that one.
            var author = FirstNonEmpty(audio.AlbumArtist, audio.Artist);
            if (!string.IsNullOrWhiteSpace(author))
            {
                metadata.Authors = SplitPeople(author);
                metadata.Author = metadata.Authors.FirstOrDefault();
            }

            if (!string.IsNullOrWhiteSpace(audio.Narrator))
            {
                metadata.Narrators = SplitPeople(audio.Narrator);
                metadata.Narrator = metadata.Narrators.FirstOrDefault();
            }

            if (!string.IsNullOrWhiteSpace(audio.Genre))
            {
                metadata.Genres = SplitPeople(audio.Genre);
            }

            if (!string.IsNullOrWhiteSpace(audio.Isbn))
            {
                metadata.Isbn = [audio.Isbn];
            }

            metadata.ImageUrl = await CacheEmbeddedCoverAsync(filePath, metadata);
            return metadata;
        }

        private async Task<string?> CacheEmbeddedCoverAsync(string filePath, AudibleBookMetadata metadata)
        {
            var cover = _coverExtractor.TryExtract(filePath);
            if (cover == null)
            {
                return null;
            }

            // A book with no ASIN still needs a stable cache key, so it is derived from the
            // file path: the same file re-read returns the same cached cover rather than
            // accumulating a new copy on every preview.
            var identifier = !string.IsNullOrWhiteSpace(metadata.Asin)
                ? metadata.Asin!
                : BuildPathIdentifier(filePath);

            var cached = await _imageCache.CacheImageBytesAsync(cover.Bytes, identifier, cover.MediaType);
            if (cached == null)
            {
                _logger.LogDebug(
                    "Embedded cover from {File} could not be cached",
                    LogRedaction.SanitizeFilePath(filePath));
            }

            return cached;
        }

        private static string BuildPathIdentifier(string filePath)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(filePath));
            return string.Concat("embedded-", Convert.ToHexString(hash)[..16].ToLowerInvariant());
        }

        private static string? FormatSeriesPosition(decimal? position) =>
            position?.ToString("0.##", CultureInfo.InvariantCulture);

        private static List<string> SplitPeople(string value) =>
            value
                .Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static string? NullIfBlank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string FirstNonEmpty(params string?[] candidates) =>
            candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim() ?? string.Empty;

        private const string EmbeddedMetadataSource = "EmbeddedFile";
    }
}
