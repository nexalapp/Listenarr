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
using Listenarr.Application.Audiobooks.AutoGrab;
using Listenarr.Application.Audiobooks.Discover;
using Listenarr.Application.Audiobooks.Suggestions;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.AutoGrab
{
    /// <summary>
    /// What a run adds, and in what order.
    ///
    /// <para>
    /// This adds books to somebody's library without being asked each time, so the
    /// promises worth pinning are the ones that bound it: only a book a check already
    /// found a release for, never more than the run allows, each book once however many
    /// lists offer it, and the lists drawn from in the order they were put in.
    /// </para>
    /// </summary>
    [Trait("Name", "AutoGrabServiceTests")]
    [Trait("Category", "Application")]
    public sealed class AutoGrabServiceTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<ISuggestionService> _suggestions = new();
        private readonly Mock<IDiscoverService> _discover = new();
        private readonly Mock<ISuggestionReleaseRepository> _releases = new();
        private readonly Mock<ILibraryAddService> _library = new();
        private readonly Mock<IAbookThankBudget> _thanks = new();
        private readonly ApplicationSettings _settings = new();
        private readonly Dictionary<string, SuggestionRelease> _found = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<LibraryAddOperationRequest> _added = [];

        private readonly List<SuggestedBook> _authorBooks = [];
        private readonly List<SuggestedBook> _seriesBooks = [];
        private readonly List<DiscoverSectionFeed> _chartSections = [];

        private AutoGrabService BuildService()
        {
            _configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(_settings);
            _suggestions.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new SuggestionSnapshot(
                    [new AuthorSuggestionGroup("An author", null, null, 1, false, _authorBooks)],
                    [new SeriesSuggestionGroup("A series", null, 1, false, _seriesBooks)],
                    [],
                    new SuggestionCoverage(0, 0, 0, 0),
                    []));
            _discover.Setup(d => d.GetFeedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string source, CancellationToken _) => new DiscoverFeed(
                    source, _chartSections, 5, true, null, DiscoverRefreshStatus.Idle));
            _releases.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _found);
            _library
                .Setup(l => l.AddToLibraryAsync(
                    It.IsAny<LibraryAddOperationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LibraryAddOperationRequest request, CancellationToken _) =>
                {
                    _added.Add(request);
                    return new LibraryAddOperationResult
                    {
                        Added = true,
                        DownloadSent = true,
                        Message = "Added"
                    };
                });

            _thanks
                .Setup(t => t.CheckAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbookThankDecision(true, null, 0, null));

            return new AutoGrabService(
                _configuration.Object,
                _suggestions.Object,
                _discover.Object,
                _releases.Object,
                _library.Object,
                _thanks.Object,
                new FixedClock(Now),
                NullLogger<AutoGrabService>.Instance);
        }

        [Fact]
        public async Task Preview_WithNothingTicked_PlansNothingAndSaysWhy()
        {
            Configure(enabled: true);
            Offer(_seriesBooks, "B1", "Drive");
            Found("B1");

            var plan = await BuildService().PreviewAsync();

            Assert.Empty(plan.Candidates);
            Assert.Equal("No list is ticked.", plan.Unavailable);
        }

        [Fact]
        public async Task Preview_OffersOnlyBooksACheckFoundAReleaseFor()
        {
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            Offer(_seriesBooks, "B1", "Has a release");
            Offer(_seriesBooks, "B2", "Nobody holds this");
            Found("B1");

            var plan = await BuildService().PreviewAsync();

            Assert.Equal(["B1"], plan.Candidates.Select(candidate => candidate.Asin));
        }

        [Fact]
        public async Task Preview_LeavesOutABookWhoseCheckCameBackNotFound()
        {
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            Offer(_seriesBooks, "B1", "Checked and absent");
            _found["B1"] = new SuggestionRelease { Asin = "B1", Status = SuggestionReleaseStatus.NotFound };

            Assert.Empty((await BuildService().PreviewAsync()).Candidates);
        }

        [Fact]
        public async Task Preview_DrawsFromTheListsInTheOrderTheyWereGiven()
        {
            Configure(enabled: true,
                new AutoGrabSource(AutoGrabSources.Discover, "1"),
                new AutoGrabSource(AutoGrabSources.Series));
            Offer(_seriesBooks, "B1", "From the series");
            Chart("1", "Science Fiction & Fantasy", ("B2", "From the chart"));
            Found("B1", "B2");

            var plan = await BuildService().PreviewAsync();

            Assert.Equal(["B2", "B1"], plan.Candidates.Select(candidate => candidate.Asin));
        }

        [Fact]
        public async Task Preview_OffersABookOnceHoweverManyListsHaveIt()
        {
            Configure(enabled: true,
                new AutoGrabSource(AutoGrabSources.Series),
                new AutoGrabSource(AutoGrabSources.Discover, "1"));
            Offer(_seriesBooks, "B1", "Drive");
            Chart("1", "Science Fiction & Fantasy", ("B1", "Drive"));
            Found("B1");

            var plan = await BuildService().PreviewAsync();

            var candidate = Assert.Single(plan.Candidates);
            // The list it is counted against is the one that gets to it first.
            Assert.Equal("From your series", candidate.SourceLabel);
        }

        [Fact]
        public async Task Preview_PlansEveryWaitingBook()
        {
            // There is no count per run any more: what a run does is bounded by the
            // lanes it draws through, not by a number chosen in advance.
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            for (var index = 1; index <= 5; index++)
            {
                Offer(_seriesBooks, $"B{index}", $"Book {index}");
                Found($"B{index}");
            }

            var plan = await BuildService().PreviewAsync();

            Assert.Equal(5, plan.Candidates.Count);
            Assert.Equal(5, plan.Available);
        }

        [Fact]
        public async Task Preview_CountsWhatEachListContributed()
        {
            Configure(enabled: true,
                new AutoGrabSource(AutoGrabSources.Series),
                new AutoGrabSource(AutoGrabSources.Discover, "1"));
            Offer(_seriesBooks, "B1", "One");
            Chart("1", "Science Fiction & Fantasy", ("B2", "Two"), ("B3", "Three"));
            Found("B1", "B2", "B3");

            var plan = await BuildService().PreviewAsync();

            Assert.Equal(
                [("From your series", 1), ("Science Fiction & Fantasy", 2)],
                plan.BySource.Select(count => (count.Label, count.Available)));
        }

        [Fact]
        public async Task Run_AddsEachBookMonitoredAndSearching()
        {
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            Offer(_seriesBooks, "B1", "Drive", author: "James S. A. Corey", image: "https://example.invalid/c.jpg");
            Found("B1");

            var report = await BuildService().RunAsync();

            var request = Assert.Single(_added);
            // Monitored and AutoSearch together are what make the add path send the
            // release the check already found, instead of searching again.
            Assert.True(request.Monitored);
            Assert.True(request.AutoSearch);
            Assert.Equal("B1", request.Metadata.Asin);
            Assert.Equal("Drive", request.Metadata.Title);
            Assert.Equal("James S. A. Corey", Assert.Single(request.Metadata.Authors!));
            // The catalogue record travels with it: a book added by a run looks like the
            // same book added by hand.
            Assert.Equal("https://example.invalid/c.jpg", request.Metadata.ImageUrl);
            Assert.Equal(1, report.Added);
        }

        [Fact]
        public async Task Run_TakesEverythingWaitingWhenTheLanesHaveRoom()
        {
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            for (var index = 1; index <= 10; index++)
            {
                Offer(_seriesBooks, $"B{index}", $"Book {index}");
                Found($"B{index}");
            }

            var report = await BuildService().RunAsync();

            Assert.Equal(10, _added.Count);
            Assert.Equal(10, report.Added);
            Assert.Equal(10, report.Downloaded);
            Assert.Null(report.StoppedBecause);
        }

        [Fact]
        public async Task Run_StopsWhenTheThankBudgetIsSpentRatherThanAddingBooksItCannotFetch()
        {
            // The books here all come from abook.link, so each costs a thank. Once the
            // budget refuses, adding the rest would leave books in the library that
            // nothing will download - which is exactly the backlog this replaced.
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            for (var index = 1; index <= 5; index++)
            {
                Offer(_seriesBooks, $"B{index}", $"Book {index}");
                Found($"B{index}");
            }

            var service = BuildService();
            var asked = 0;
            _thanks
                .Setup(t => t.CheckAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ++asked <= 2
                    ? new AbookThankDecision(true, null, asked, null)
                    : new AbookThankDecision(false, "the hourly thank limit", 120, null));

            var report = await service.RunAsync();

            Assert.Equal(2, _added.Count);
            Assert.Equal(2, report.Added);
            Assert.Equal(3, report.Deferred);
            Assert.Equal("the hourly thank limit", report.StoppedBecause);
        }

        [Fact]
        public async Task Run_WithTheThankBudgetSpentStillTakesTheReleasesThatNeedNoThank()
        {
            // The order is stable, so ending the run at the first refused thank would
            // strand every Newznab release behind it - this run, and every run after.
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            Offer(_seriesBooks, "B1", "Needs a thank");
            Found("B1");
            Offer(_seriesBooks, "B2", "Needs no thank");
            _found["B2"] = new SuggestionRelease
            {
                Asin = "B2",
                Status = SuggestionReleaseStatus.Found,
                IndexerName = "NZBgeek (Prowlarr)",
                ReleaseTitle = "Some release"
            };

            var service = BuildService();
            _thanks
                .Setup(t => t.CheckAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbookThankDecision(false, "spent", 120, null));

            var report = await service.RunAsync();

            Assert.Equal(1, report.Added);
            Assert.Equal(1, report.Deferred);
            Assert.Equal("Needs no thank", Assert.Single(_added).Metadata!.Title);
        }

        [Fact]
        public async Task Run_DoesNotSpendTheThankBudgetOnAReleaseThatNeedsNoThank()
        {
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            Offer(_seriesBooks, "B1", "From a Newznab indexer");
            _found["B1"] = new SuggestionRelease
            {
                Asin = "B1",
                Status = SuggestionReleaseStatus.Found,
                IndexerName = "NZBgeek (Prowlarr)",
                ReleaseTitle = "Some release"
            };

            var service = BuildService();
            _thanks
                .Setup(t => t.CheckAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbookThankDecision(false, "spent", 120, null));

            var report = await service.RunAsync();

            // A Newznab release posts nothing to anybody's forum, so a spent thank
            // budget is none of its business.
            Assert.Equal(1, report.Added);
            Assert.Null(report.StoppedBecause);
        }

        [Fact]
        public async Task Run_KeepsGoingWhenOneBookWillNotAdd()
        {
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            Offer(_seriesBooks, "B1", "Breaks");
            Offer(_seriesBooks, "B2", "Fine");
            Found("B1", "B2");

            // Built first: the general setup inside would otherwise win over this one,
            // because Moq takes the setup registered last.
            var service = BuildService();
            _library
                .Setup(l => l.AddToLibraryAsync(
                    It.Is<LibraryAddOperationRequest>(request => request.Metadata.Asin == "B1"),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("no root folder"));

            var report = await service.RunAsync();

            Assert.Equal(1, report.Added);
            Assert.Equal(1, report.Failed);
            Assert.Contains(report.Outcomes, outcome => outcome.Error == "no root folder");
        }

        [Fact]
        public async Task Run_DoesNotCountABookAlreadyHeldAsAFailure()
        {
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            Offer(_seriesBooks, "B1", "Already yours");
            Found("B1");

            var service = BuildService();
            _library
                .Setup(l => l.AddToLibraryAsync(
                    It.IsAny<LibraryAddOperationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LibraryAddOperationResult { Added = false, AlreadyExists = true });

            var report = await service.RunAsync();

            Assert.Equal(0, report.Added);
            Assert.Null(Assert.Single(report.Outcomes).Error);
        }

        private void Configure(bool enabled, params AutoGrabSource[] sources) =>
            _settings.AutoGrabSettingsJson = AutoGrabSettingsJson.Write(
                new AutoGrabSettings(enabled, 60, sources));

        private static void Offer(
            List<SuggestedBook> into,
            string asin,
            string title,
            string author = "An author",
            string? image = null) =>
            into.Add(new SuggestedBook(
                asin, title, null, [author], [], image, null, null, null, [], null, null, null, null, null,
                "Audible", null, null, null, null));

        private void Chart(string sectionId, string name, params (string Asin, string Title)[] books) =>
            _chartSections.Add(new DiscoverSectionFeed(
                sectionId, name, 1, null, null, null, 0,
                books.Select((book, index) => new DiscoverEntry(
                    index + 1, null, null, null, true,
                    new SuggestedBook(
                        book.Asin, book.Title, null, ["An author"], [], null, null, null, null, [],
                        null, null, null, null, null, "Audible", null, null, null, null)))
                    .ToList()));

        private void Found(params string[] asins)
        {
            foreach (var asin in asins)
            {
                _found[asin] = new SuggestionRelease
                {
                    Asin = asin,
                    Status = SuggestionReleaseStatus.Found,
                    IndexerName = "Abook.link",
                    ReleaseTitle = $"Some release for {asin}"
                };
            }
        }

        private sealed class FixedClock(DateTime utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        }
    
        [Fact]
        public async Task Preview_PutsTheReleasesThatCostNothingFirst()
        {
            // Measured on the live library: the queue was 91% abook.link, so a Newznab
            // release was reached at about one in eleven - stuck behind books that are
            // rationed, when it is free and could be fetched at once.
            Configure(enabled: true, new AutoGrabSource(AutoGrabSources.Series));
            foreach (var asin in new[] { "A1", "A2", "A3" })
            {
                Offer(_seriesBooks, asin, $"Abook {asin}");
                Found(asin);
            }

            Offer(_seriesBooks, "N1", "Newznab one");
            _found["N1"] = new SuggestionRelease
            {
                Asin = "N1",
                Status = SuggestionReleaseStatus.Found,
                IndexerName = "NZBgeek (Prowlarr)",
                ReleaseTitle = "Free to grab"
            };

            var plan = await BuildService().PreviewAsync();

            Assert.Equal("N1", plan.Candidates[0].Asin);
            Assert.Equal(4, plan.Candidates.Count);
            // The rationed ones keep the order their lists gave them.
            Assert.Equal(["A1", "A2", "A3"], plan.Candidates.Skip(1).Select(c => c.Asin));
        }
    }
}
