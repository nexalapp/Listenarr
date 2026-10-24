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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Discover
{
    /// <summary>
    /// The discovery tabs make the same promise the rest of the Suggested page does:
    /// what they list is not already on the shelf, and not something the user has said
    /// no to. These pin that, and the one thing that is particular to a chart — that a
    /// book's place in it does not move when the books above it are filtered out.
    /// </summary>
    [Trait("Name", "DiscoverServiceTests")]
    [Trait("Category", "Application")]
    public sealed class DiscoverServiceTests : BaseTests
    {
        private readonly Mock<IDiscoverRepository> _discover = new();
        private readonly Mock<IAudiobookRepository> _repository = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IDiscoverRefreshService> _refresh = new();
        private readonly StubReader _charts = new(DiscoverSources.AudibleCharts);
        private readonly StubReader _abook = new(DiscoverSources.AbookLink);
        private readonly ApplicationSettings _settings = new();
        private readonly List<Audiobook> _library = [];
        private readonly List<SuggestionDismissal> _dismissals = [];
        private readonly List<DiscoverSection> _sections = [];
        private readonly List<DiscoverItem> _items = [];

        private DiscoverService BuildService()
        {
            _repository.Setup(r => r.GetAllAsync()).ReturnsAsync(_library);
            _repository
                .Setup(r => r.GetAllSeriesMembershipsGroupedByAudiobookIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<int, List<AudiobookSeriesMembership>>());
            _repository
                .Setup(r => r.GetSuggestionDismissalsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_dismissals);
            _configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(_settings);
            _configuration
                .Setup(c => c.SaveApplicationSettingsAsync(It.IsAny<ApplicationSettings>()))
                .Returns(Task.CompletedTask);
            _discover
                .Setup(d => d.GetSectionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string source, CancellationToken _) =>
                    _sections.Where(section => section.Source == source).ToList());
            _discover
                .Setup(d => d.GetItemsAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string source, IReadOnlyCollection<string> ids, CancellationToken _) =>
                    _items.Where(item => item.Source == source && ids.Contains(item.SectionId)).ToList());
            _discover
                .Setup(d => d.PruneSectionsAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);
            _refresh.Setup(r => r.StatusFor(It.IsAny<string>())).Returns(DiscoverRefreshStatus.Idle);

            return new DiscoverService(
                _discover.Object,
                _repository.Object,
                _configuration.Object,
                _refresh.Object,
                new DiscoverSectionCatalogCache(),
                [_charts, _abook]);
        }

        [Fact]
        public async Task GetFeed_WithNothingFollowed_SaysSoWithoutReadingTheCache()
        {
            var feed = await BuildService().GetFeedAsync(DiscoverSources.AudibleCharts);

            Assert.Empty(feed.Sections);
            Assert.True(feed.Available);
            _discover.Verify(
                d => d.GetItemsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task GetFeed_LeavesOutABookTheLibraryAlreadyHasAndCountsIt()
        {
            FollowChart();
            _library.Add(new Audiobook { Id = 1, Title = "Dungeon Crawler Carl", Authors = ["Matt Dinniman"] });
            Cached(DiscoverSources.AudibleCharts, "1", "B1", 1, "Dungeon Crawler Carl", "Matt Dinniman");
            Cached(DiscoverSources.AudibleCharts, "1", "B2", 2, "Drive", "James S. A. Corey");

            var section = Assert.Single((await BuildService().GetFeedAsync(DiscoverSources.AudibleCharts)).Sections);

            Assert.Equal(1, section.Held);
            Assert.Equal("Drive", Assert.Single(section.Books).Book.Title);
        }

        [Fact]
        public async Task GetFeed_KeepsABooksPlaceInTheChartWhenTheOnesAboveItAreHeld()
        {
            FollowChart();
            _library.Add(new Audiobook { Id = 1, Title = "Dungeon Crawler Carl", Authors = ["Matt Dinniman"] });
            Cached(DiscoverSources.AudibleCharts, "1", "B1", 1, "Dungeon Crawler Carl", "Matt Dinniman");
            Cached(DiscoverSources.AudibleCharts, "1", "B2", 2, "Drive", "James S. A. Corey");

            var section = Assert.Single((await BuildService().GetFeedAsync(DiscoverSources.AudibleCharts)).Sections);

            // Second in the chart, and still second once the first is filtered out.
            Assert.Equal(2, Assert.Single(section.Books).Rank);
        }

        [Fact]
        public async Task GetFeed_LeavesOutABookTheUserSaidNoTo()
        {
            FollowChart();
            Cached(DiscoverSources.AudibleCharts, "1", "B1", 1, "Drive", "James S. A. Corey");
            _dismissals.Add(new SuggestionDismissal { Key = "asin:B1", Title = "Drive" });

            var section = Assert.Single((await BuildService().GetFeedAsync(DiscoverSources.AudibleCharts)).Sections);

            // Ignoring a book on one tab ignores it on all of them.
            Assert.Empty(section.Books);
            Assert.Equal(0, section.Held);
        }

        [Fact]
        public async Task GetFeed_LeavesOutABookInALanguageTheLibraryDoesNotRead()
        {
            FollowChart();
            _settings.LibraryLanguagesJson = """["english"]""";
            Cached(DiscoverSources.AudibleCharts, "1", "B1", 1, "Drive", "James S. A. Corey", language: "italian");
            Cached(DiscoverSources.AudibleCharts, "1", "B2", 2, "Nemesis Games", "James S. A. Corey", language: "english");

            var section = Assert.Single((await BuildService().GetFeedAsync(DiscoverSources.AudibleCharts)).Sections);

            Assert.Equal("Nemesis Games", Assert.Single(section.Books).Book.Title);
        }

        [Fact]
        public async Task GetFeed_FromABoard_PutsTheNewestFirstWhateverOrderItWasRead()
        {
            Follow(DiscoverSources.AbookLink, new DiscoverSelectedSection("7", "Science Fiction"));
            Cached(DiscoverSources.AbookLink, "7", "123455", 1, "Men of War", "William R. Forstchen",
                postedAt: new DateTime(2026, 10, 22, 22, 45, 0, DateTimeKind.Utc));
            Cached(DiscoverSources.AbookLink, "7", "123463", 2, "Valiant", "Jack Campbell",
                postedAt: new DateTime(2026, 10, 23, 12, 16, 0, DateTimeKind.Utc));

            var section = Assert.Single((await BuildService().GetFeedAsync(DiscoverSources.AbookLink)).Sections);

            Assert.Equal(["Valiant", "Men of War"], section.Books.Select(book => book.Book.Title));
        }

        [Fact]
        public async Task GetFeed_FromASourceThatCannotBeUsed_SaysWhy()
        {
            Follow(DiscoverSources.AbookLink, new DiscoverSelectedSection("7", "Science Fiction"));
            _abook.Unavailable = "No abook.link source is configured.";

            var feed = await BuildService().GetFeedAsync(DiscoverSources.AbookLink);

            Assert.False(feed.Available);
            Assert.Equal("No abook.link source is configured.", feed.Unavailable);
        }

        [Fact]
        public async Task GetFeed_ShowsWhatTheLastFetchOfASectionLeftBehind()
        {
            FollowChart();
            _sections.Add(new DiscoverSection
            {
                Source = DiscoverSources.AudibleCharts,
                SectionId = "1",
                SectionName = "Science Fiction & Fantasy",
                PagesFetched = 2,
                TotalItems = 100,
                LastError = "Audible's charts could not be read.",
                LastFetchedAt = new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc)
            });

            var section = Assert.Single((await BuildService().GetFeedAsync(DiscoverSources.AudibleCharts)).Sections);

            Assert.Equal(2, section.PagesFetched);
            Assert.Equal(100, section.TotalItems);
            Assert.Equal("Audible's charts could not be read.", section.LastError);
        }

        [Fact]
        public async Task GetSections_OffersWhatTheSourceListsAndMarksWhatIsFollowed()
        {
            FollowChart();
            _charts.Sections =
            [
                new DiscoverSectionOption("1", "Science Fiction & Fantasy", "sff"),
                new DiscoverSectionOption("2", "Romance", "romance")
            ];

            var catalog = await BuildService().GetSectionsAsync(DiscoverSources.AudibleCharts);

            Assert.Equal(["1", "2"], catalog.Options.Select(option => option.Id));
            Assert.Equal(["1"], catalog.Selected);
        }

        [Fact]
        public async Task GetSections_KeepsAFollowedSectionTheSourceNoLongerLists()
        {
            FollowChart();
            _charts.Sections = [new DiscoverSectionOption("2", "Romance", "romance")];

            var catalog = await BuildService().GetSectionsAsync(DiscoverSources.AudibleCharts);

            // Otherwise it cannot be unfollowed, and its cached books are unexplained.
            Assert.Contains(catalog.Options, option => option.Id == "1");
        }

        [Fact]
        public async Task SaveSelection_StoresTheChoiceAndForgetsWhatIsNoLongerFollowed()
        {
            FollowChart();
            var service = BuildService();

            ApplicationSettings? saved = null;
            _configuration
                .Setup(c => c.SaveApplicationSettingsAsync(It.IsAny<ApplicationSettings>()))
                .Callback<ApplicationSettings>(settings => saved = settings)
                .Returns(Task.CompletedTask);

            var selection = await service.SaveSelectionAsync(
                DiscoverSources.AudibleCharts,
                new SaveDiscoverSelectionRequest([new DiscoverSelectedSection("2", "Romance", "romance")], 4));

            Assert.Equal("2", Assert.Single(selection.Sections).Id);
            Assert.NotNull(saved);
            Assert.Contains("Romance", saved!.DiscoverSettingsJson);
            _discover.Verify(
                d => d.PruneSectionsAsync(
                    DiscoverSources.AudibleCharts,
                    It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && ids.Contains("2")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task SaveSelection_WithOnlyADepth_LeavesTheSectionsAlone()
        {
            FollowChart();

            var selection = await BuildService().SaveSelectionAsync(
                DiscoverSources.AudibleCharts,
                new SaveDiscoverSelectionRequest(null, 2));

            Assert.Equal("1", Assert.Single(selection.Sections).Id);
            Assert.Equal(2, selection.PagesPerSection);
        }

        [Fact]
        public async Task GetFeed_OfASourceThatDoesNotExist_IsRefused()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => BuildService().GetFeedAsync("goodreads"));
        }

        private void FollowChart() =>
            Follow(
                DiscoverSources.AudibleCharts,
                new DiscoverSelectedSection("1", "Science Fiction & Fantasy", "sff"));

        private void Follow(string source, params DiscoverSelectedSection[] sections) =>
            _settings.DiscoverSettingsJson = DiscoverSettings.Write(
                _settings.DiscoverSettingsJson, source, new DiscoverSelection(sections, 5));

        private void Cached(
            string source,
            string sectionId,
            string externalId,
            int rank,
            string title,
            string author,
            string? language = null,
            DateTime? postedAt = null) =>
            _items.Add(new DiscoverItem
            {
                Id = _items.Count + 1,
                Source = source,
                SectionId = sectionId,
                ExternalId = externalId,
                Rank = rank,
                Page = 1,
                PostedAt = postedAt,
                Enriched = true,
                Book = new DiscoverBook
                {
                    Asin = externalId,
                    Title = title,
                    Authors = [author],
                    Language = language
                }
            });

        /// <summary>A source that answers from memory; nothing here goes to a network.</summary>
        private sealed class StubReader(string source) : IDiscoverSourceReader
        {
            public string Source { get; } = source;

            public string? Unavailable { get; set; }

            public IReadOnlyList<DiscoverSectionOption> Sections { get; set; } = [];

            public Task<string?> UnavailableReasonAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(Unavailable);

            public Task<IReadOnlyList<DiscoverSectionOption>> ListSectionsAsync(
                CancellationToken cancellationToken = default) =>
                Task.FromResult(Sections);

            public Task<DiscoverPage> GetPageAsync(
                DiscoverSelectedSection section,
                int page,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(new DiscoverPage(true, [], null, false, null));
        }
    }
}
