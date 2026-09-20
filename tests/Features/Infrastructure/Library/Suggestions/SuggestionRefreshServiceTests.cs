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
using Listenarr.Application.Audiobooks.Suggestions;
using Listenarr.Infrastructure.Library.Suggestions;
using Listenarr.Infrastructure.Library.Suggestions.Workers;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Suggestions
{
    /// <summary>
    /// The background fetch's promise is one catalog per interval and never on top of
    /// a refresh: the point is a cache that fills without anyone's search waiting
    /// behind it.
    /// </summary>
    [Trait("Name", "SuggestionRefreshServiceTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class SuggestionRefreshServiceTests : BaseTests
    {
        private readonly Mock<IAudiobookRepository> _repository = new();
        private readonly Mock<IAuthorCatalogService> _authors = new();
        private readonly Mock<ISeriesCatalogService> _series = new();
        private readonly List<Audiobook> _library = [];
        private readonly List<AuthorCacheEntry> _cachedAuthors = [];
        private readonly List<SeriesCacheEntry> _cachedSeries = [];
        private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));

        private SuggestionRefreshService BuildService()
        {
            _repository.Setup(r => r.GetAllAsync()).ReturnsAsync(_library);
            _repository
                .Setup(r => r.GetAllSeriesMembershipsGroupedByAudiobookIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<int, List<AudiobookSeriesMembership>>());
            _repository
                .Setup(r => r.GetAllCachedAuthorsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_cachedAuthors);
            _repository
                .Setup(r => r.GetAllCachedSeriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_cachedSeries);

            var services = new ServiceCollection();
            services.AddSingleton(_repository.Object);
            services.AddSingleton(_authors.Object);
            services.AddSingleton(_series.Object);
            var provider = services.BuildServiceProvider();
            return new SuggestionRefreshService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<SuggestionRefreshService>.Instance,
                _time);
        }

        private void Held(string author, string? series = null) =>
            _library.Add(new Audiobook
            {
                Id = _library.Count + 1,
                Title = $"Book {_library.Count + 1}",
                Authors = [author],
                Series = series,
            });

        private void Cached(string author) =>
            _cachedAuthors.Add(new AuthorCacheEntry
            {
                AuthorName = author,
                AuthorNameNormalized = SuggestionNames.Normalize(author),
                LastFetchedAt = _time.GetUtcNow().UtcDateTime,
                CatalogBooks = [new CachedAuthorCatalogBook { Title = "x", RatingOverall = 4, Description = "d" }],
            });

        [Fact]
        public async Task FetchNext_FetchesOneMissingAuthorAndReportsIt()
        {
            Held("Ann Leckie");
            Held("Becky Chambers");
            var service = BuildService();

            var fetched = await service.FetchNextNeededAsync();

            Assert.Equal("Ann Leckie", fetched);
            _authors.Verify(
                a => a.GetCatalogAsync("Ann Leckie", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), false, It.IsAny<CancellationToken>()),
                Times.Once);
            _authors.Verify(
                a => a.GetCatalogAsync("Becky Chambers", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task FetchNext_SkipsCachedNamesAndMovesOnToSeries()
        {
            Held("Ann Leckie", "Imperial Radch");
            Cached("Ann Leckie");
            var service = BuildService();

            var fetched = await service.FetchNextNeededAsync();

            Assert.Equal("Imperial Radch", fetched);
            _series.Verify(
                s => s.GetCatalogAsync("Imperial Radch", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), false, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task FetchNext_ReturnsNullWhenNothingIsMissing()
        {
            Held("Ann Leckie");
            Cached("Ann Leckie");
            var service = BuildService();

            Assert.Null(await service.FetchNextNeededAsync());
            _authors.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task FetchNext_StandsAsideWhileARefreshIsRunning()
        {
            Held("Ann Leckie");
            Held("Becky Chambers");
            var hold = new TaskCompletionSource<AuthorCatalogFetchResult?>();
            _authors
                .Setup(a => a.GetCatalogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(hold.Task);
            var service = BuildService();

            Assert.True(await service.RequestRefreshAsync());
            Assert.True(service.Status.Running);

            Assert.Null(await service.FetchNextNeededAsync());

            hold.SetResult(null);
        }

        [Fact]
        public async Task Processor_FetchesOncePerIntervalAndNotWhenOff()
        {
            var refresh = new Mock<ISuggestionRefreshService>();
            refresh.Setup(r => r.FetchNextNeededAsync(It.IsAny<CancellationToken>())).ReturnsAsync("Ann Leckie");
            var settings = new ApplicationSettings { SuggestionsBackgroundFetchIntervalMinutes = 5 };
            var configuration = new Mock<IConfigurationService>();
            configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);
            var services = new ServiceCollection();
            services.AddSingleton(configuration.Object);
            var provider = services.BuildServiceProvider();
            var processor = new SuggestionCatalogProcessor(
                NullLogger<SuggestionCatalogProcessor>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>(),
                refresh.Object,
                _time);

            await processor.RunCycleAsync(CancellationToken.None);
            _time.Advance(TimeSpan.FromMinutes(1));
            await processor.RunCycleAsync(CancellationToken.None);
            refresh.Verify(r => r.FetchNextNeededAsync(It.IsAny<CancellationToken>()), Times.Once);

            _time.Advance(TimeSpan.FromMinutes(4));
            await processor.RunCycleAsync(CancellationToken.None);
            refresh.Verify(r => r.FetchNextNeededAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));

            settings.SuggestionsBackgroundFetchIntervalMinutes = 0;
            _time.Advance(TimeSpan.FromMinutes(10));
            await processor.RunCycleAsync(CancellationToken.None);
            refresh.Verify(r => r.FetchNextNeededAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
        {
            private DateTimeOffset _utcNow = utcNow;

            public override DateTimeOffset GetUtcNow() => _utcNow;

            public void Advance(TimeSpan by) => _utcNow += by;
        }
    }
}
