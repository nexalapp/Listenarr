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
using Listenarr.Application.Audiobooks.Discover;
using Listenarr.Domain.Audiobooks.Discover;
using Listenarr.Infrastructure.Library.Discover;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Discover
{
    /// <summary>
    /// What the background fetcher does next, and why.
    ///
    /// <para>
    /// The order is the whole design: the head of a section goes stale first, because
    /// that is where anything new appears; then the posts nobody has looked up, because
    /// a row with no cover is half a row; then depth. These pin that order, and the two
    /// rules that stop it running for ever — the page limit, and a source that says it
    /// has no more.
    /// </para>
    /// </summary>
    [Trait("Name", "DiscoverRefreshServiceTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class DiscoverRefreshServiceTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

        private readonly Mock<IDiscoverRepository> _discover = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IDiscoverMetadataResolver> _resolver = new();
        private readonly ApplicationSettings _settings = new();
        private readonly List<DiscoverSection> _sections = [];
        private readonly List<DiscoverItem> _unresolved = [];
        private readonly RecordingReader _charts = new(DiscoverSources.AudibleCharts);
        private readonly RecordingReader _abook = new(DiscoverSources.AbookLink);
        private readonly MutableClock _time = new(Now);

        private DiscoverRefreshService BuildService()
        {
            _configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(_settings);
            _discover
                .Setup(d => d.GetSectionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string source, CancellationToken _) =>
                    _sections.Where(section => section.Source == source).ToList());
            _discover
                .Setup(d => d.GetUnresolvedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string source, int limit, CancellationToken _) =>
                    _unresolved.Where(item => item.Source == source).Take(limit).ToList());
            _discover
                .Setup(d => d.UpsertItemsAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<DiscoverItem>>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);
            _discover
                .Setup(d => d.TrimSectionAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);
            _discover
                .Setup(d => d.ResolveItemAsync(
                    It.IsAny<int>(), It.IsAny<DiscoverBook>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _resolver
                .Setup(r => r.ResolveAsync(It.IsAny<DiscoverBook>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DiscoverBook?)null);

            var services = new ServiceCollection();
            services.AddSingleton(_discover.Object);
            services.AddSingleton(_configuration.Object);
            services.AddSingleton(_resolver.Object);
            services.AddSingleton<IDiscoverSourceReader>(_charts);
            services.AddSingleton<IDiscoverSourceReader>(_abook);
            var provider = services.BuildServiceProvider();

            return new DiscoverRefreshService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<DiscoverRefreshService>.Instance,
                _time);
        }

        [Fact]
        public async Task FetchNextNeeded_WithNothingFollowed_DoesNothing()
        {
            Assert.Null(await BuildService().FetchNextNeededAsync());
            Assert.Empty(_charts.Requested);
        }

        [Fact]
        public async Task FetchNextNeeded_ReadsTheHeadOfASectionNobodyHasRead()
        {
            Follow(DiscoverSources.AudibleCharts, ("1", "Science Fiction & Fantasy"));

            await BuildService().FetchNextNeededAsync();

            Assert.Equal([("1", 1)], _charts.Requested);
        }

        [Fact]
        public async Task FetchNextNeeded_ReadsAStaleHeadBeforeAnythingElse()
        {
            Follow(DiscoverSources.AudibleCharts, ("1", "Science Fiction & Fantasy"));
            Cached("1", pagesFetched: 1, lastFetchedAt: Now.AddHours(-7));
            Unresolved(DiscoverSources.AudibleCharts);

            await BuildService().FetchNextNeededAsync();

            // Six hours is the point at which what the chart says is worth asking again.
            Assert.Equal([("1", 1)], _charts.Requested);
            _resolver.Verify(
                r => r.ResolveAsync(It.IsAny<DiscoverBook>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task FetchNextNeeded_LooksUpAPostBeforeReadingFurtherBack()
        {
            Follow(DiscoverSources.AbookLink, ("7", "Science Fiction"));
            Cached("7", pagesFetched: 1, lastFetchedAt: Now.AddMinutes(-5), source: DiscoverSources.AbookLink);
            Unresolved(DiscoverSources.AbookLink);

            await BuildService().FetchNextNeededAsync();

            // A page nobody has read is worth less than a cover on a row already shown.
            Assert.Empty(_abook.Requested);
            _resolver.Verify(
                r => r.ResolveAsync(It.IsAny<DiscoverBook>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FetchNextNeeded_ThenTakesTheNextPageNobodyHasRead()
        {
            Follow(DiscoverSources.AudibleCharts, ("1", "Science Fiction & Fantasy"));
            Cached("1", pagesFetched: 2, lastFetchedAt: Now.AddMinutes(-5));

            await BuildService().FetchNextNeededAsync();

            Assert.Equal([("1", 3)], _charts.Requested);
        }

        [Fact]
        public async Task FetchNextNeeded_StopsAtTheConfiguredDepth()
        {
            Follow(DiscoverSources.AudibleCharts, ("1", "Science Fiction & Fantasy"), pages: 2);
            Cached("1", pagesFetched: 2, lastFetchedAt: Now.AddMinutes(-5));

            Assert.Null(await BuildService().FetchNextNeededAsync());
            Assert.Empty(_charts.Requested);
        }

        [Fact]
        public async Task FetchNextNeeded_StopsWhenTheSourceSaysThereIsNoMore()
        {
            Follow(DiscoverSources.AudibleCharts, ("1", "Science Fiction & Fantasy"), pages: 5);
            Cached("1", pagesFetched: 2, lastFetchedAt: Now.AddMinutes(-5), exhausted: true);

            // Audible answers a page past the end by repeating the last one, so "no
            // more" has to be remembered rather than rediscovered every cycle.
            Assert.Null(await BuildService().FetchNextNeededAsync());
        }

        [Fact]
        public async Task FetchNextNeeded_LeavesASourceThatCannotBeUsedAlone()
        {
            Follow(DiscoverSources.AbookLink, ("7", "Science Fiction"));
            _abook.Unavailable = "No abook.link source is configured.";

            Assert.Null(await BuildService().FetchNextNeededAsync());
            Assert.Empty(_abook.Requested);
        }

        [Fact]
        public async Task FetchPage_RanksABooksPlaceByThePageItCameFrom()
        {
            Follow(DiscoverSources.AudibleCharts, ("1", "Science Fiction & Fantasy"));
            Cached("1", pagesFetched: 1, lastFetchedAt: Now.AddMinutes(-5));
            _charts.Items = [PageItem("B21"), PageItem("B22")];

            await BuildService().FetchNextNeededAsync();

            _discover.Verify(
                d => d.UpsertItemsAsync(
                    DiscoverSources.AudibleCharts,
                    "1",
                    It.Is<IReadOnlyList<DiscoverItem>>(items => items[0].Rank == 3 && items[1].Rank == 4),
                    2,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task FetchPage_ForABoard_AddsRatherThanReplacingThePage()
        {
            Follow(DiscoverSources.AbookLink, ("7", "Science Fiction"));
            _abook.Items = [PageItem("123")];

            await BuildService().FetchNextNeededAsync();

            // A new post pushes older topics onto the next page; nothing was withdrawn.
            _discover.Verify(
                d => d.UpsertItemsAsync(
                    DiscoverSources.AbookLink,
                    "7",
                    It.IsAny<IReadOnlyList<DiscoverItem>>(),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _discover.Verify(
                d => d.TrimSectionAsync(
                    DiscoverSources.AbookLink, "7", It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task FetchPage_WhenTheSourceFails_RecordsWhyAndKeepsWhatWasRead()
        {
            Follow(DiscoverSources.AudibleCharts, ("1", "Science Fiction & Fantasy"));
            Cached("1", pagesFetched: 2, lastFetchedAt: Now.AddHours(-7));
            _charts.Failure = "Audible's charts could not be read.";

            await BuildService().FetchNextNeededAsync();

            _discover.Verify(
                d => d.SaveSectionAsync(
                    DiscoverSources.AudibleCharts,
                    "1",
                    It.IsAny<string>(),
                    2,
                    It.IsAny<int?>(),
                    It.IsAny<bool>(),
                    "Audible's charts could not be read.",
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ResolveItem_ThatNothingMatches_IsStillMarkedLookedUp()
        {
            Follow(DiscoverSources.AbookLink, ("7", "Science Fiction"));
            Cached("7", pagesFetched: 1, lastFetchedAt: Now.AddMinutes(-5), source: DiscoverSources.AbookLink);
            Unresolved(DiscoverSources.AbookLink);

            await BuildService().FetchNextNeededAsync();

            // Otherwise the same fruitless search runs every cycle, for ever.
            _discover.Verify(
                d => d.ResolveItemAsync(
                    42, It.IsAny<DiscoverBook>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RequestRefresh_WithNothingFollowed_IsRefused()
        {
            Assert.False(await BuildService().RequestRefreshAsync(DiscoverSources.AudibleCharts));
        }

        [Fact]
        public async Task RequestMore_WhenEverySectionIsAsDeepAsAllowed_IsRefused()
        {
            Follow(DiscoverSources.AudibleCharts, ("1", "Science Fiction & Fantasy"), pages: 1);
            Cached("1", pagesFetched: 1, lastFetchedAt: Now);

            Assert.False(await BuildService().RequestMoreAsync(DiscoverSources.AudibleCharts));
        }

        [Fact]
        public void StatusFor_AnUnknownSource_IsIdleRatherThanAThrow()
        {
            Assert.False(BuildService().StatusFor("goodreads").Running);
        }

        private void Follow(string source, (string Id, string Name) section, int pages = 5) =>
            _settings.DiscoverSettingsJson = DiscoverSettings.Write(
                _settings.DiscoverSettingsJson,
                source,
                new DiscoverSelection([new DiscoverSelectedSection(section.Id, section.Name)], pages));

        private void Cached(
            string sectionId,
            int pagesFetched,
            DateTime? lastFetchedAt,
            bool exhausted = false,
            string source = DiscoverSources.AudibleCharts) =>
            _sections.Add(new DiscoverSection
            {
                Source = source,
                SectionId = sectionId,
                SectionName = sectionId,
                PagesFetched = pagesFetched,
                Exhausted = exhausted,
                LastFetchedAt = lastFetchedAt
            });

        private void Unresolved(string source) =>
            _unresolved.Add(new DiscoverItem
            {
                Id = 42,
                Source = source,
                SectionId = "7",
                ExternalId = "123",
                Enriched = false,
                Book = new DiscoverBook { Title = "Valiant", Authors = ["Jack Campbell"] }
            });

        private static DiscoverPageItem PageItem(string externalId) =>
            new(externalId, new DiscoverBook { Asin = externalId, Title = externalId });

        /// <summary>A source that answers from memory and remembers what it was asked for.</summary>
        private sealed class RecordingReader(string source) : IDiscoverSourceReader
        {
            public string Source { get; } = source;

            public string? Unavailable { get; set; }

            public string? Failure { get; set; }

            public IReadOnlyList<DiscoverPageItem> Items { get; set; } = [];

            public List<(string SectionId, int Page)> Requested { get; } = [];

            public Task<string?> UnavailableReasonAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(Unavailable);

            public Task<IReadOnlyList<DiscoverSectionOption>> ListSectionsAsync(
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<DiscoverSectionOption>>([]);

            public Task<DiscoverPage> GetPageAsync(
                DiscoverSelectedSection section,
                int page,
                CancellationToken cancellationToken = default)
            {
                Requested.Add((section.Id, page));
                return Task.FromResult(
                    Failure is { Length: > 0 }
                        ? DiscoverPage.Failed(Failure)
                        : new DiscoverPage(true, Items, null, true, null));
            }
        }

        private sealed class MutableClock(DateTime utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        }
    }
}
