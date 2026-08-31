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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Monitoring
{
    [Trait("Name", "SeriesMonitoringAnnouncedBooksTests")]
    [Trait("Category", "SeriesMonitoringService")]
    public class SeriesMonitoringAnnouncedBooksTests : BaseTests
    {
        private readonly Mock<ISeriesCatalogService> _seriesCatalogService = new();
        private readonly Mock<ILibraryAddService> _libraryAddService = new();

        // Audible lists unreleased titles in a series' relationships with a future
        // release_date; this is the shape one of those arrives in.
        private const string AnnouncedAsin = "B0G24H25QQ";
        private const string AnnouncedTitle = "Once a Crown";
        private const string AnnouncedReleaseDate = "2028-01-11";

        [Fact]
        public async Task SyncSeriesAsync_AddsAnAnnouncedBook_WithItsFutureReleaseDate()
        {
            // Given a monitored series whose catalogue contains one released and one
            // announced book
            Init(services => services
                .WithSingleton(_seriesCatalogService.Object)
                .WithSingleton(_libraryAddService.Object));

            GivenCatalog();
            var added = GivenAddPersistsToTheLibrary();

            var service = _provider.GetRequiredService<ISeriesMonitoringService>();

            // When
            var result = await service.MonitorSeriesAsync(new MonitorSeriesRequest
            {
                Name = "A Carrick Hall Novel",
                Region = "us",
                Language = "english"
            });

            // Then the unreleased book is added like any other, carrying its date
            Assert.True(result.SyncResult.Succeeded);
            Assert.Equal(2, result.SyncResult.AddedCount);

            var announced = Assert.Single(added, request => request.Metadata.Asin == AnnouncedAsin);
            Assert.Equal(AnnouncedTitle, announced.Metadata.Title);
            Assert.Equal(AnnouncedReleaseDate, announced.Metadata.PublishedDate);
            Assert.True(announced.Monitored);
            Assert.False(announced.AutoSearch);

            var stored = Assert.Single(
                await _audiobookRepository.GetAllAsync(),
                book => book.Asin == AnnouncedAsin);
            Assert.Equal(AnnouncedReleaseDate, stored.PublishedDate);
        }

        [Fact]
        public async Task SyncSeriesAsync_PollingTwice_DoesNotDuplicateAnAnnouncedBook()
        {
            // Given a series already synced once
            Init(services => services
                .WithSingleton(_seriesCatalogService.Object)
                .WithSingleton(_libraryAddService.Object));

            GivenCatalog();
            GivenAddPersistsToTheLibrary();

            var service = _provider.GetRequiredService<ISeriesMonitoringService>();
            var first = await service.MonitorSeriesAsync(new MonitorSeriesRequest
            {
                Name = "A Carrick Hall Novel",
                Region = "us",
                Language = "english"
            });

            Assert.Equal(2, first.SyncResult.AddedCount);

            // When the same series is polled again, as the scheduler does
            var second = await service.SyncSeriesAsync(first.MonitoredSeries!.Id);

            // Then nothing is added a second time and the library still holds two books.
            // An announced book sits in the library for months before release, so every
            // poll in that window has to recognise it.
            Assert.True(second.Succeeded);
            Assert.Equal(0, second.AddedCount);
            Assert.Equal(2, second.ExistingCount);

            var library = await _audiobookRepository.GetAllAsync();
            Assert.Equal(2, library.Count);
            Assert.Single(library, book => book.Asin == AnnouncedAsin);
        }

        [Fact]
        public async Task GetAllMonitoredSeriesAsync_SurfacesAMonitorThatIsFailing()
        {
            // Given a monitor whose catalogue lookup fails
            Init(services => services
                .WithSingleton(_seriesCatalogService.Object)
                .WithSingleton(_libraryAddService.Object));

            _seriesCatalogService
                .Setup(catalog => catalog.GetCatalogAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((SeriesCatalogFetchResult?)null);

            var service = _provider.GetRequiredService<ISeriesMonitoringService>();
            var result = await service.MonitorSeriesAsync(new MonitorSeriesRequest
            {
                Name = "A Carrick Hall Novel",
                Region = "us",
                Language = "english"
            });

            Assert.False(result.SyncResult.Succeeded);

            // When the calendar asks which monitors exist
            var monitors = await service.GetAllMonitoredSeriesAsync();

            // Then the failure is readable, rather than presenting as an empty calendar
            var monitor = Assert.Single(monitors);
            Assert.Equal("A Carrick Hall Novel", monitor.SeriesName);
            Assert.False(string.IsNullOrWhiteSpace(monitor.LastError));
            Assert.NotNull(monitor.LastCheckedAt);
            Assert.Null(monitor.LastSuccessfulSyncAt);
        }

        [Fact]
        public async Task GetAllMonitoredSeriesAsync_ReportsAHealthyMonitor_WithNoError()
        {
            Init(services => services
                .WithSingleton(_seriesCatalogService.Object)
                .WithSingleton(_libraryAddService.Object));

            GivenCatalog();
            GivenAddPersistsToTheLibrary();

            var service = _provider.GetRequiredService<ISeriesMonitoringService>();
            await service.MonitorSeriesAsync(new MonitorSeriesRequest
            {
                Name = "A Carrick Hall Novel",
                Region = "us",
                Language = "english"
            });

            var monitor = Assert.Single(await service.GetAllMonitoredSeriesAsync());
            Assert.Null(monitor.LastError);
            Assert.NotNull(monitor.LastSuccessfulSyncAt);
        }

        private void GivenCatalog()
        {
            _seriesCatalogService
                .Setup(catalog => catalog.GetCatalogAsync(
                    "A Carrick Hall Novel",
                    "us",
                    500,
                    null,
                    true,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SeriesCatalogFetchResultBuilder()
                    .WithSeries("A Carrick Hall Novel", "B0D59YSN2V")
                    .WithBook(new AudibleSearchResultBuilder()
                        .WithAsin("B0B2HD3C3D")
                        .WithTitle("Once a Castle")
                        .WithAuthor("Sarah Arthur")
                        .WithLanguage("english")
                        .WithReleaseDate("2023-06-13")
                        .WithSeries("A Carrick Hall Novel", "1", "B0D59YSN2V")
                        .Build())
                    .WithBook(new AudibleSearchResultBuilder()
                        .WithAsin(AnnouncedAsin)
                        .WithTitle(AnnouncedTitle)
                        .WithAuthor("Sarah Arthur")
                        .WithLanguage("english")
                        .WithReleaseDate(AnnouncedReleaseDate)
                        .WithSeries("A Carrick Hall Novel", "3", "B0D59YSN2V")
                        .Build())
                    .Build());
        }

        /// <summary>
        /// The real add service writes the book to the library; the dedupe on the next poll
        /// reads it back, so a mock that only returns a result would make the duplicate test
        /// pass for the wrong reason.
        /// </summary>
        private List<LibraryAddOperationRequest> GivenAddPersistsToTheLibrary()
        {
            var requests = new List<LibraryAddOperationRequest>();

            _libraryAddService
                .Setup(add => add.AddToLibraryAsync(
                    It.IsAny<LibraryAddOperationRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (LibraryAddOperationRequest request, CancellationToken _) =>
                {
                    requests.Add(request);

                    var book = new AudiobookBuilder()
                        .WithTitle(request.Metadata.Title ?? string.Empty)
                        .WithMonitored()
                        .Build();
                    book.Asin = request.Metadata.Asin;
                    book.PublishedDate = request.Metadata.PublishedDate;
                    book.Authors = request.Metadata.Authors;

                    var saved = await _audiobookRepository.AddAsync(book);

                    return new LibraryAddOperationResult
                    {
                        Added = true,
                        Message = "Audiobook added to library successfully",
                        Audiobook = saved
                    };
                });

            return requests;
        }
    }
}
