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

namespace Listenarr.Tests.Features.Infrastructure.Images.Cache
{
    [Trait("Name", "ImageCacheServiceTests")]
    [Trait("Category", "Cache")]
    public class ImageCacheServiceTests : BaseTests
    {
        [Fact]
        public async Task MoveToAuthorLibraryStorageAsync_UsesApplicationPathServiceCachePaths()
        {
            var tempRoot = FileService.GetTempPath();
            var repoApiRoot = Path.Join(tempRoot, "listenarr.api");
            var tempCachePath = Path.Join(repoApiRoot, "config", "cache", "images", "temp");
            var libraryCachePath = Path.Join(repoApiRoot, "config", "cache", "images", "library");
            var authorCachePath = Path.Join(repoApiRoot, "config", "cache", "images", "authors");
            var seriesCachePath = Path.Join(repoApiRoot, "config", "cache", "images", "series");

            using var httpClient = new HttpClient();

            var applicationPathService = new Mock<IApplicationPathService>();
            applicationPathService.SetupGet(service => service.ContentRootPath).Returns(repoApiRoot);
            applicationPathService
                .Setup(service => service.ResolveFromConfig("cache", "images", "temp"))
                .Returns(tempCachePath);
            applicationPathService
                .Setup(service => service.ResolveFromConfig("cache", "images", "library"))
                .Returns(libraryCachePath);
            applicationPathService
                .Setup(service => service.ResolveFromConfig("cache", "images", "authors"))
                .Returns(authorCachePath);
            applicationPathService
                .Setup(service => service.ResolveFromConfig("cache", "images", "series"))
                .Returns(seriesCachePath);

            var service = new ImageCacheService(
                Mock.Of<ILogger<ImageCacheService>>(),
                httpClient,
                applicationPathService.Object);

            var repoTempImage = Path.Join(tempCachePath, "AUTHOR123.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(repoTempImage)!);
            await File.WriteAllBytesAsync(repoTempImage, new byte[] { 1, 2, 3, 4 });

            var relativePath = await service.MoveToAuthorLibraryStorageAsync("AUTHOR123");

            var expectedAuthorImage = Path.Join(authorCachePath, "AUTHOR123.jpg");

            Assert.Equal("config/cache/images/authors/AUTHOR123.jpg", relativePath);
            Assert.True(File.Exists(expectedAuthorImage));
        }

        [Fact]
        public async Task MoveToAuthorLibraryStorageAsync_DoesNotEscapeCacheRootForTraversalIdentifier()
        {
            var tempRoot = FileService.GetTempPath();
            var repoApiRoot = Path.Join(tempRoot, "listenarr.api");
            var tempCachePath = Path.Join(repoApiRoot, "config", "cache", "images", "temp");
            var libraryCachePath = Path.Join(repoApiRoot, "config", "cache", "images", "library");
            var authorCachePath = Path.Join(repoApiRoot, "config", "cache", "images", "authors");
            var seriesCachePath = Path.Join(repoApiRoot, "config", "cache", "images", "series");
            var outsidePath = Path.Join(repoApiRoot, "config", "cache", "images", "outside.jpg");

            using var httpClient = new HttpClient();
            var applicationPathService = new Mock<IApplicationPathService>();
            applicationPathService.SetupGet(service => service.ContentRootPath).Returns(repoApiRoot);
            applicationPathService.Setup(service => service.ResolveFromConfig("cache", "images", "temp")).Returns(tempCachePath);
            applicationPathService.Setup(service => service.ResolveFromConfig("cache", "images", "library")).Returns(libraryCachePath);
            applicationPathService.Setup(service => service.ResolveFromConfig("cache", "images", "authors")).Returns(authorCachePath);
            applicationPathService.Setup(service => service.ResolveFromConfig("cache", "images", "series")).Returns(seriesCachePath);

            var service = new ImageCacheService(
                Mock.Of<ILogger<ImageCacheService>>(),
                httpClient,
                applicationPathService.Object);

            var relativePath = await service.MoveToAuthorLibraryStorageAsync("../outside");

            Assert.Null(relativePath);
            Assert.False(File.Exists(outsidePath));
        }

        private (ImageCacheService Service, string TempCache, string LibraryCache) BuildService()
        {
            var repoApiRoot = Path.Join(FileService.GetTempPath(), "listenarr.api");
            var tempCachePath = Path.Join(repoApiRoot, "config", "cache", "images", "temp");
            var libraryCachePath = Path.Join(repoApiRoot, "config", "cache", "images", "library");

            var applicationPathService = new Mock<IApplicationPathService>();
            applicationPathService.SetupGet(s => s.ContentRootPath).Returns(repoApiRoot);
            applicationPathService.Setup(s => s.ResolveFromConfig("cache", "images", "temp")).Returns(tempCachePath);
            applicationPathService.Setup(s => s.ResolveFromConfig("cache", "images", "library")).Returns(libraryCachePath);
            applicationPathService
                .Setup(s => s.ResolveFromConfig("cache", "images", "authors"))
                .Returns(Path.Join(repoApiRoot, "config", "cache", "images", "authors"));
            applicationPathService
                .Setup(s => s.ResolveFromConfig("cache", "images", "series"))
                .Returns(Path.Join(repoApiRoot, "config", "cache", "images", "series"));

            var service = new ImageCacheService(
                Mock.Of<ILogger<ImageCacheService>>(),
                new HttpClient(),
                applicationPathService.Object);

            return (service, tempCachePath, libraryCachePath);
        }

        [Fact]
        public async Task CacheImageBytesAsync_StoresBytesThatHaveNoUrlToDownloadFrom()
        {
            var (service, tempCache, _) = BuildService();

            var relativePath = await service.CacheImageBytesAsync(
                JpegBytes(),
                "embedded-deadbeef",
                "image/jpeg");

            Assert.Equal("config/cache/images/temp/embedded-deadbeef.jpg", relativePath);
            Assert.True(File.Exists(Path.Join(tempCache, "embedded-deadbeef.jpg")));
        }

        [Fact]
        public async Task MoveToLibraryStorageAsync_PromotesAnAlreadyCachedFileUnderANewKey()
        {
            // Cover art extracted from an audiobook file is keyed by the file, not by an ASIN
            // the book does not have, so the add flow asks to move it under a different key and
            // has to say where the bytes already are. Before this, the move looked for a temp
            // file under the new key, missed, tried to download the local path as a URL, and
            // left the book pointing at a temp path that the UI could not render.
            var (service, tempCache, libraryCache) = BuildService();

            var sourceRelativePath = await service.CacheImageBytesAsync(
                JpegBytes(),
                "embedded-cafebabe",
                "image/jpeg");
            Assert.NotNull(sourceRelativePath);

            var promoted = await service.MoveToLibraryStorageAsync("img-1234abcd", sourceRelativePath);

            Assert.Equal("config/cache/images/library/img-1234abcd.jpg", promoted);
            Assert.True(File.Exists(Path.Join(libraryCache, "img-1234abcd.jpg")));
            Assert.False(File.Exists(Path.Join(tempCache, "embedded-cafebabe.jpg")));
        }

        [Fact]
        public async Task MoveToLibraryStorageAsync_RefusesASourceOutsideTheCache()
        {
            // The source path reaches this from a request body, so a traversal attempt must
            // not be able to pull an arbitrary file into library storage.
            var (service, _, libraryCache) = BuildService();

            var promoted = await service.MoveToLibraryStorageAsync(
                "img-escape",
                "config/cache/images/temp/../../../../etc/hosts");

            Assert.Null(promoted);
            Assert.False(File.Exists(Path.Join(libraryCache, "img-escape.jpg")));
        }

        private static byte[] JpegBytes()
        {
            // A JPEG header followed by enough bytes to clear the placeholder-image check.
            var bytes = new byte[2048];
            bytes[0] = 0xFF;
            bytes[1] = 0xD8;
            bytes[2] = 0xFF;
            return bytes;
        }

    }
}
