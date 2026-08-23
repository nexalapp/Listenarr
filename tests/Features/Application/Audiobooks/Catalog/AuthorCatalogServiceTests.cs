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

namespace Listenarr.Tests.Features.Application.Audiobooks.Catalog
{
    public class AuthorCatalogServiceTests
    {
        private static readonly HttpClient SharedHttpClient = new();

        [Fact]
        public async Task GetCatalogAsync_SupplementsSparseFallbackCatalog_WithSearchResults()
        {
            var audible = new Mock<AudibleService>(SharedHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var searchService = new Mock<ISearchService>();
            var logger = new Mock<ILogger<AuthorCatalogService>>();

            audible
                .Setup(service => service.LookupAuthorAsync("Andy Weir", "us"))
                .ReturnsAsync(new AuthorLookupItem
                {
                    Asin = "B00G0WYW92",
                    Name = "Andy Weir"
                });

            audible
                .Setup(service => service.GetAllBooksByAuthorAsync("Andy Weir", "B00G0WYW92", 10, "us", null))
                .ReturnsAsync(new AudibleSearchResponse
                {
                    Results = new List<AudibleSearchResult>
                    {
                        new()
                        {
                            Asin = "B08G9PRS1K",
                            Title = "Project Hail Mary",
                            Authors = new List<AudibleAuthor> { new() { Name = "Andy Weir" } },
                            ImageUrl = "project-hail-mary.jpg"
                        }
                    },
                    TotalResults = 1
                });

            searchService
                .Setup(service => service.IntelligentSearchAsync(
                    "Andy Weir",
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    "Relaxed",
                    false,
                    0.7,
                    "us",
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MetadataSearchResult>
                {
                    new()
                    {
                        Asin = "B08G9PRS1K",
                        Title = "Project Hail Mary",
                        Artist = "Andy Weir",
                        ImageUrl = "project-hail-mary.jpg"
                    },
                    new()
                    {
                        Asin = "B00B5HZGUG",
                        Title = "The Martian",
                        Artist = "Andy Weir",
                        ImageUrl = "the-martian.jpg",
                        ProductUrl = "https://example.com/the-martian"
                    },
                    new()
                    {
                        Asin = "B01MAUQJ1H",
                        Title = "Artemis",
                        Artist = "Andy Weir",
                        ImageUrl = "artemis.jpg",
                        ProductUrl = "https://example.com/artemis"
                    },
                    new()
                    {
                        Asin = "B000OTHER1",
                        Title = "Dark Matter",
                        Artist = "Blake Crouch"
                    }
                });

            var service = new AuthorCatalogService(
                audible.Object,
                audnexus.Object,
                audiobookRepository.Object,
                searchService.Object,
                logger.Object);

            var result = await service.GetCatalogAsync("Andy Weir", "us", 10);

            Assert.NotNull(result);
            Assert.Collection(
                result!.Books,
                book => Assert.Equal("Project Hail Mary", book.Title),
                book => Assert.Equal("The Martian", book.Title),
                book => Assert.Equal("Artemis", book.Title));
        }

        [Fact]
        public async Task GetCatalogAsync_UsesPersistedCatalogCache_BeforeAudible()
        {
            var audible = new Mock<AudibleService>(SharedHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var searchService = new Mock<ISearchService>();
            var logger = new Mock<ILogger<AuthorCatalogService>>();

            audiobookRepository
                .Setup(repository => repository.GetCachedAuthorByNameAsync("Andy Weir", "us"))
                .ReturnsAsync(new AuthorCacheEntry
                {
                    AuthorName = "Andy Weir",
                    AuthorNameNormalized = "andy weir",
                    AuthorAsin = "B00G0WYW92",
                    Region = "us",
                    ImageUrl = "andy-weir.jpg",
                    CatalogBooks = new List<CachedAuthorCatalogBook>
                    {
                        new()
                        {
                            Asin = "B08G9PRS1K",
                            Title = "Project Hail Mary",
                            Authors = new List<string> { "Andy Weir" },
                            Language = "en-us"
                        },
                        new()
                        {
                            Asin = "B00B5HZGUG",
                            Title = "The Martian",
                            Authors = new List<string> { "Andy Weir" },
                            Language = "de"
                        }
                    }
                });

            var service = new AuthorCatalogService(
                audible.Object,
                audnexus.Object,
                audiobookRepository.Object,
                searchService.Object,
                logger.Object);

            var result = await service.GetCatalogAsync("Andy Weir", "us", 10, "english");

            Assert.NotNull(result);
            Assert.Single(result!.Books);
            Assert.Equal("Project Hail Mary", result.Books[0].Title);
            Assert.Equal("Andy Weir", result.Author.Name);

            audible.Verify(service => service.LookupAuthorAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            audible.Verify(service => service.GetBooksByAuthorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
            audible.Verify(service => service.GetAllBooksByAuthorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task GetCatalogAsync_ForceRefresh_BypassesPersistedCatalogCache_AndPersistsFreshBooks()
        {
            var audible = new Mock<AudibleService>(SharedHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var searchService = new Mock<ISearchService>();
            var logger = new Mock<ILogger<AuthorCatalogService>>();

            audiobookRepository
                .Setup(repository => repository.GetCachedAuthorByNameAsync("Brandon Sanderson", "us"))
                .ReturnsAsync(new AuthorCacheEntry
                {
                    AuthorName = "Brandon Sanderson",
                    AuthorNameNormalized = "brandon sanderson",
                    AuthorAsin = "B001IGFHW6",
                    Region = "us",
                    ImageUrl = "old-image.jpg",
                    CatalogBooks = new List<CachedAuthorCatalogBook>
                    {
                        new()
                        {
                            Title = "Old Cached Book",
                            Authors = new List<string> { "Brandon Sanderson" },
                            MetadataSource = "OpenLibrary"
                        }
                    }
                });

            audiobookRepository
                .Setup(repository => repository.UpsertCachedAuthorAsync(It.IsAny<AuthorCacheEntry>()))
                .ReturnsAsync((AuthorCacheEntry entry) => entry);

            audible
                .Setup(service => service.GetAllBooksByAuthorAsync("Brandon Sanderson", "B001IGFHW6", 10, "us", null))
                .ReturnsAsync(new AudibleSearchResponse
                {
                    Results = new List<AudibleSearchResult>
                    {
                        new()
                        {
                            Asin = "B002V8H13U",
                            Title = "The Way of Kings",
                            Authors = new List<AudibleAuthor> { new() { Name = "Brandon Sanderson" } },
                            ImageUrl = "way-of-kings.jpg",
                            Language = "english",
                            Link = "https://audible.example/way-of-kings"
                        }
                    },
                    TotalResults = 1
                });

            var service = new AuthorCatalogService(
                audible.Object,
                audnexus.Object,
                audiobookRepository.Object,
                searchService.Object,
                logger.Object);

            var result = await service.GetCatalogAsync("Brandon Sanderson", "us", 10, forceRefresh: true);

            Assert.NotNull(result);
            Assert.Single(result!.Books);
            Assert.Equal("The Way of Kings", result.Books[0].Title);

            audible.Verify(
                svc => svc.GetAllBooksByAuthorAsync("Brandon Sanderson", "B001IGFHW6", 10, "us", null),
                Times.Once);
            audiobookRepository.Verify(
                repository => repository.UpsertCachedAuthorAsync(It.Is<AuthorCacheEntry>(entry =>
                    entry.AuthorAsin == "B001IGFHW6" &&
                    entry.CatalogBooks != null &&
                    entry.CatalogBooks.Count == 1 &&
                    entry.CatalogBooks[0].Title == "The Way of Kings")),
                Times.Once);
        }

        [Fact]
        public void MapCachedCatalogBook_RoundTripsEverySeriesMembership()
        {
            var book = new AudibleSearchResult
            {
                Asin = "B017V4JA2Q",
                Title = "Harry Potter and the Prisoner of Azkaban",
                Series = new List<AudibleSeries>
                {
                    new() { Name = "Harry Potter", Position = "3" },
                    new() { Name = "Wizarding World Collection", Position = "3" }
                }
            };

            var cached = AuthorCatalogMapping.MapCachedCatalogBook(book);
            var restored = AuthorCatalogMapping.MapCachedCatalogBook(cached);

            // Collapsing to the first membership is what split a series across groups and
            // made each half look incomplete.
            Assert.Equal(2, cached.SeriesMemberships!.Count);
            Assert.Equal(2, restored.Series!.Count);
            Assert.Contains(restored.Series, series => series.Name == "Harry Potter" && series.Position == "3");
            Assert.Contains(restored.Series, series => series.Name == "Wizarding World Collection");
        }

        [Fact]
        public void MapCachedCatalogBook_EntryCachedBeforeMembershipsExisted_StillResolves()
        {
            // Rows written by an earlier version carry only the flat pair and no membership
            // list; they must keep working rather than losing their series entirely.
            var legacy = new CachedAuthorCatalogBook
            {
                Asin = "LEGACY1",
                Title = "Cached Before Memberships",
                Series = "Mistborn",
                SeriesNumber = "1",
                SeriesMemberships = null
            };

            var restored = AuthorCatalogMapping.MapCachedCatalogBook(legacy);

            var series = Assert.Single(restored.Series!);
            Assert.Equal("Mistborn", series.Name);
            Assert.Equal("1", series.Position);
        }
    }
}
