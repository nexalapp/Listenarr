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
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Catalog
{
    /// <summary>
    /// A book no metadata provider can match still has to reach the library, and the only
    /// source of truth left is the file. These cover the shape that produces: the fields
    /// the add flow needs, and the cover art that has no URL to be fetched from.
    /// </summary>
    [Trait("Name", "EmbeddedFileMetadataServiceTests")]
    [Trait("Category", "Application")]
    public class EmbeddedFileMetadataServiceTests : BaseTests
    {
        private const string FilePath = "/audiobooks/Author/Book/book.m4b";

        private static EmbeddedFileMetadataService BuildService(
            AudioMetadata? audio,
            EmbeddedCover? cover = null,
            Mock<IImageCacheService>? imageCache = null)
        {
            var metadataService = new Mock<IMetadataService>();
            metadataService
                .Setup(s => s.ExtractFileMetadataAsync(It.IsAny<string>()))
                .ReturnsAsync(audio);

            var extractor = new Mock<IEmbeddedCoverExtractor>();
            extractor.Setup(e => e.TryExtract(It.IsAny<string>())).Returns(cover);

            imageCache ??= new Mock<IImageCacheService>();

            return new EmbeddedFileMetadataService(
                metadataService.Object,
                extractor.Object,
                imageCache.Object,
                NullLogger<EmbeddedFileMetadataService>.Instance);
        }

        [Fact]
        public async Task ReadAsync_MapsTheFieldsAManualImportDependsOn()
        {
            var audio = new AudioMetadata
            {
                Title = "Drive",
                Subtitle = "The Expanse, Book 0.1",
                AlbumArtist = "James S. A. Corey",
                Narrator = "Jefferson Mays",
                Description = "A novella.",
                Genre = "Science Fiction",
                Publisher = "Orbit",
                Series = "The Expanse",
                SeriesPosition = 0.1m,
                Year = 2022,
                Duration = TimeSpan.FromMinutes(58),
            };

            var metadata = await BuildService(audio).ReadAsync(FilePath);

            Assert.NotNull(metadata);
            Assert.Equal("Drive", metadata!.Title);
            Assert.Equal("The Expanse, Book 0.1", metadata.Subtitle);
            Assert.Equal(["James S. A. Corey"], metadata.Authors);
            Assert.Equal(["Jefferson Mays"], metadata.Narrators);
            Assert.Equal("A novella.", metadata.Description);
            Assert.Equal("The Expanse", metadata.Series);
            Assert.Equal("0.1", metadata.SeriesNumber);
            Assert.Equal("2022", metadata.PublishYear);
            Assert.Equal(58, metadata.Runtime);
        }

        [Fact]
        public async Task ReadAsync_SplitsAFullCastNarratorListIntoSeparateNames()
        {
            // Audiobook taggers put every reader in the single composer field, comma
            // separated. A full-cast recording lists all of them.
            var audio = new AudioMetadata
            {
                Title = "Chronicles of Narnia Intro",
                Artist = "C. S. Lewis",
                Narrator = "Kenneth Branagh, Alex Jennings, Michael York",
            };

            var metadata = await BuildService(audio).ReadAsync(FilePath);

            Assert.Equal(
                ["Kenneth Branagh", "Alex Jennings", "Michael York"],
                metadata!.Narrators);
            Assert.Equal("Kenneth Branagh", metadata.Narrator);
        }

        [Fact]
        public async Task ReadAsync_FallsBackToTheAlbumWhenTheFileHasNoTitle()
        {
            var audio = new AudioMetadata { Album = "[Known Space 00.0] Beclaimed in Hell" };

            var metadata = await BuildService(audio).ReadAsync(FilePath);

            Assert.Equal("[Known Space 00.0] Beclaimed in Hell", metadata!.Title);
        }

        [Fact]
        public async Task ReadAsync_CachesTheEmbeddedCoverUnderAPathDerivedKeyWhenThereIsNoAsin()
        {
            // Without an ASIN there is no natural cache key, so it comes from the file path:
            // re-reading the same file must reuse its cover rather than accumulate copies.
            var capturedKeys = new List<string>();
            var imageCache = new Mock<IImageCacheService>();
            imageCache
                .Setup(c => c.CacheImageBytesAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>()))
                .Callback<byte[], string, string?>((_, key, _) => capturedKeys.Add(key))
                .ReturnsAsync("config/cache/images/temp/embedded-abc.jpg");

            var service = BuildService(
                new AudioMetadata { Title = "Untraceable" },
                new EmbeddedCover([1, 2, 3], "image/jpeg"),
                imageCache);

            var first = await service.ReadAsync(FilePath);
            var second = await service.ReadAsync(FilePath);

            Assert.Equal("config/cache/images/temp/embedded-abc.jpg", first!.ImageUrl);
            Assert.Equal(first.ImageUrl, second!.ImageUrl);

            // Both reads must ask for the same key, or the same file would be cached twice.
            var keys = capturedKeys.Distinct(StringComparer.Ordinal).ToList();
            Assert.Single(keys);
            Assert.StartsWith("embedded-", keys[0], StringComparison.Ordinal);
        }

        [Fact]
        public async Task ReadAsync_KeysTheCoverByAsinWhenTheFileCarriesOne()
        {
            var imageCache = new Mock<IImageCacheService>();
            imageCache
                .Setup(c => c.CacheImageBytesAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync("config/cache/images/temp/B09P45M3XH.jpg");

            var service = BuildService(
                new AudioMetadata { Title = "Drive", Asin = "B09P45M3XH" },
                new EmbeddedCover([1], "image/png"),
                imageCache);

            await service.ReadAsync(FilePath);

            imageCache.Verify(
                c => c.CacheImageBytesAsync(It.IsAny<byte[]>(), "B09P45M3XH", "image/png"),
                Times.Once);
        }

        [Fact]
        public async Task ReadAsync_LeavesTheImageUnsetWhenTheFileHasNoCover()
        {
            var metadata = await BuildService(new AudioMetadata { Title = "Coverless" }).ReadAsync(FilePath);

            Assert.NotNull(metadata);
            Assert.Null(metadata!.ImageUrl);
        }

        [Fact]
        public async Task ReadAsync_ReturnsNullWhenTheFileYieldsNoMetadata()
        {
            Assert.Null(await BuildService(audio: null).ReadAsync(FilePath));
        }
    }
}
