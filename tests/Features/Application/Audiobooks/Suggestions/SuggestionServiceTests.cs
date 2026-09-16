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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Suggestions
{
    /// <summary>
    /// The page's one promise is that what it lists is not already on the shelf. These
    /// pin every way a catalog book can be recognised as held, and that the page reads
    /// only from what is cached.
    /// </summary>
    [Trait("Name", "SuggestionServiceTests")]
    [Trait("Category", "Application")]
    public sealed class SuggestionServiceTests : BaseTests
    {
        private readonly Mock<IAudiobookRepository> _repository = new();
        private readonly Mock<IAuthorMonitoringService> _authorMonitoring = new();
        private readonly Mock<ISeriesMonitoringService> _seriesMonitoring = new();
        private readonly List<MonitoredAuthor> _monitoredAuthors = [];
        private readonly List<SuggestionDismissal> _dismissals = [];
        private readonly List<Audiobook> _library = [];
        private readonly List<AuthorCacheEntry> _authors = [];
        private readonly List<SeriesCacheEntry> _series = [];

        private SuggestionService BuildService()
        {
            _repository.Setup(r => r.GetAllAsync()).ReturnsAsync(_library);
            _repository
                .Setup(r => r.GetAllSeriesMembershipsGroupedByAudiobookIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_library
                    .Where(book => book.SeriesMemberships is { Count: > 0 })
                    .ToDictionary(book => book.Id, book => book.SeriesMemberships!));
            _repository
                .Setup(r => r.GetAllCachedAuthorsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_authors);
            _repository
                .Setup(r => r.GetAllCachedSeriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_series);
            _repository
                .Setup(r => r.GetSuggestionDismissalsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_dismissals);
            _repository
                .Setup(r => r.AddSuggestionDismissalAsync(It.IsAny<SuggestionDismissal>(), It.IsAny<CancellationToken>()))
                .Callback<SuggestionDismissal, CancellationToken>((d, _) => _dismissals.Add(d))
                .Returns(Task.CompletedTask);
            _authorMonitoring
                .Setup(m => m.GetAllMonitoredAuthorsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_monitoredAuthors);
            _seriesMonitoring
                .Setup(m => m.GetAllMonitoredSeriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            return new SuggestionService(_repository.Object, _authorMonitoring.Object, _seriesMonitoring.Object);
        }

        private int _nextId = 1;

        private Audiobook Held(
            string title,
            string author,
            string? asin = null,
            string? isbn = null,
            string? series = null)
        {
            var book = new Audiobook
            {
                Id = _nextId++,
                Title = title,
                Authors = [author],
                Asin = asin,
                Isbn = isbn == null ? null : [isbn],
                Series = series,
                SeriesMemberships = series == null
                    ? null
                    : [new AudiobookSeriesMembership { SeriesName = series, IsPrimary = true }]
            };
            _library.Add(book);
            return book;
        }

        private static CachedAuthorCatalogBook Catalog(
            string title,
            string author,
            string? asin = null,
            string? isbn = null) =>
            new() { Title = title, Authors = [author], Asin = asin, Isbn = isbn };

        private void CachedAuthor(string name, params CachedAuthorCatalogBook[] books) =>
            _authors.Add(new AuthorCacheEntry
            {
                AuthorName = name,
                AuthorNameNormalized = SuggestionNames.Normalize(name),
                CatalogBooks = [.. books]
            });

        [Fact]
        public async Task Get_OffersWhatALibraryAuthorHasThatTheLibraryDoesNot()
        {
            Held("Leviathan Wakes", "James S. A. Corey", asin: "B1");
            CachedAuthor("James S. A. Corey",
                Catalog("Leviathan Wakes", "James S. A. Corey", asin: "B1"),
                Catalog("Caliban's War", "James S. A. Corey", asin: "B2"));

            var snapshot = await BuildService().GetAsync();

            var group = Assert.Single(snapshot.Authors);
            Assert.Equal("James S. A. Corey", group.Author);
            Assert.Equal(1, group.LibraryCount);
            Assert.Equal("Caliban's War", Assert.Single(group.Missing).Title);
        }

        [Fact]
        public async Task Get_IgnoresCachedAuthorsTheLibraryDoesNotHold()
        {
            // A catalog cached from browsing an author page is not a reason to suggest
            // every one of their books.
            Held("Dune", "Frank Herbert");
            CachedAuthor("Someone Else", Catalog("Their Book", "Someone Else", asin: "X1"));

            var snapshot = await BuildService().GetAsync();

            Assert.Empty(snapshot.Authors);
            Assert.Equal(0, snapshot.Coverage.AuthorsWithCatalog);
            Assert.Equal(1, snapshot.Coverage.AuthorsInLibrary);
        }

        [Theory]
        [InlineData("B1", null, "Other Title")]           // ASIN
        [InlineData(null, "978-0-316-12908-4", "Other")]  // ISBN, punctuation differs
        [InlineData(null, null, "Leviathan Wakes: A Novel")] // title before the colon + author
        [InlineData(null, null, "leviathan wakes")]         // case
        public async Task Get_DoesNotOfferABookAlreadyHeld(string? asin, string? isbn, string catalogTitle)
        {
            Held("Leviathan Wakes", "James S. A. Corey", asin: "B1", isbn: "9780316129084");
            CachedAuthor("James S. A. Corey", Catalog(catalogTitle, "James S. A. Corey", asin, isbn));

            var snapshot = await BuildService().GetAsync();

            Assert.Empty(snapshot.Authors);
        }

        [Fact]
        public async Task Get_MatchesTheAuthorNameTheWayTheCacheIsKeyed()
        {
            // The cache normalises names; a library author spelt with different
            // punctuation or spacing must still find their catalog.
            Held("Dune", "Frank  Herbert.", asin: "D1");
            CachedAuthor("Frank Herbert", Catalog("Dune Messiah", "Frank Herbert", asin: "D2"));

            var snapshot = await BuildService().GetAsync();

            Assert.Equal("Dune Messiah", Assert.Single(Assert.Single(snapshot.Authors).Missing).Title);
        }

        [Fact]
        public async Task Get_OffersTheRestOfASeriesInOrder()
        {
            Held("Caliban's War", "James S. A. Corey", asin: "B2", series: "The Expanse");
            _series.Add(new SeriesCacheEntry
            {
                SeriesName = "The Expanse",
                CatalogBooks =
                [
                    new() { Title = "Abaddon's Gate", Asin = "B3", SeriesNumber = "3", Authors = ["James S. A. Corey"] },
                    new() { Title = "Leviathan Wakes", Asin = "B1", SeriesNumber = "1", Authors = ["James S. A. Corey"] },
                    new() { Title = "Caliban's War", Asin = "B2", SeriesNumber = "2", Authors = ["James S. A. Corey"] },
                ]
            });

            var snapshot = await BuildService().GetAsync();

            var group = Assert.Single(snapshot.Series);
            Assert.Equal(["Leviathan Wakes", "Abaddon's Gate"], group.Missing.Select(book => book.Title));
            Assert.Equal(1, snapshot.Coverage.SeriesWithCatalog);
        }

        [Fact]
        public async Task Get_SuggestsSimilarAuthorsNotYetHeldAndSaysWhy()
        {
            Held("Dune", "Frank Herbert");
            Held("Hyperion", "Dan Simmons");
            _authors.Add(new AuthorCacheEntry
            {
                AuthorName = "Frank Herbert",
                SimilarAuthors = [new() { Name = "Isaac Asimov", Asin = "A1" }, new() { Name = "Dan Simmons" }]
            });
            _authors.Add(new AuthorCacheEntry
            {
                AuthorName = "Dan Simmons",
                SimilarAuthors = [new() { Name = "Isaac Asimov", Asin = "A1" }]
            });

            var snapshot = await BuildService().GetAsync();

            // Dan Simmons is held, so he is not suggested; Asimov is, with both reasons.
            var related = Assert.Single(snapshot.RelatedAuthors);
            Assert.Equal("Isaac Asimov", related.Name);
            Assert.Equal(["Frank Herbert", "Dan Simmons"], related.Because);
        }

        [Fact]
        public async Task Get_SkipsTranslationsIntoLanguagesTheLibraryDoesNotHold()
        {
            var held = Held("Empire of Silence", "Christopher Ruocchio", asin: "R1");
            held.Language = "English";
            CachedAuthor("Christopher Ruocchio",
                new() { Title = "Howling Dark", Authors = ["Christopher Ruocchio"], Asin = "R2", Language = "english" },
                new() { Title = "L'impero del silenzio", Authors = ["Christopher Ruocchio"], Asin = "R3", Language = "italian" },
                new() { Title = "Demon in White", Authors = ["Christopher Ruocchio"], Asin = "R4" });

            var snapshot = await BuildService().GetAsync();

            // Unknown language passes; a known other language does not.
            Assert.Equal(
                ["Howling Dark", "Demon in White"],
                Assert.Single(snapshot.Authors).Missing.Select(book => book.Title));
        }

        [Fact]
        public async Task Get_DoesNotLetOneStrayTitleMakeItsLanguageALibraryLanguage()
        {
            // 970 English books and one German one is an English library.
            for (var i = 0; i < 30; i++)
            {
                Held($"Book {i}", "Someone", asin: $"E{i}").Language = "english";
            }

            Held("Der Schwarm", "Frank Schätzing", asin: "G1").Language = "german";
            CachedAuthor("Frank Schätzing",
                new() { Title = "Limit", Authors = ["Frank Schätzing"], Asin = "G2", Language = "german" },
                new() { Title = "The Swarm", Authors = ["Frank Schätzing"], Asin = "G3", Language = "english" });

            var snapshot = await BuildService().GetAsync();

            Assert.Equal("The Swarm", Assert.Single(Assert.Single(snapshot.Authors).Missing).Title);
        }

        [Fact]
        public async Task Get_SkipsAnthologiesTheAuthorOnlyContributedTo()
        {
            Held("Rendezvous with Rama", "Arthur C. Clarke", asin: "C1");
            CachedAuthor("Arthur C. Clarke",
                new() { Title = "Childhood's End", Authors = ["Arthur C. Clarke"], Asin = "C2" },
                new() { Title = "100 Greatest Short Stories", Authors = ["Agatha Christie", "Margery Allingham"], Asin = "C3" });

            var snapshot = await BuildService().GetAsync();

            Assert.Equal("Childhood's End", Assert.Single(Assert.Single(snapshot.Authors).Missing).Title);
        }

        [Fact]
        public async Task Get_OffersABookOnceHoweverManyEditionsTheCatalogLists()
        {
            Held("Rendezvous with Rama", "Arthur C. Clarke", asin: "C1");
            CachedAuthor("Arthur C. Clarke",
                Catalog("Childhood's End", "Arthur C. Clarke", asin: "C2"),
                Catalog("Childhood's End", "Arthur C. Clarke", asin: "C3"),
                Catalog("Childhood's End: A Novel", "Arthur C. Clarke", asin: "C4"));

            var snapshot = await BuildService().GetAsync();

            Assert.Single(Assert.Single(snapshot.Authors).Missing);
        }

        [Fact]
        public async Task Get_SaysWhetherTheAuthorIsAlreadyMonitored()
        {
            // Monitoring already watches for new releases, so the page can say so
            // rather than offer to.
            Held("Dune", "Frank Herbert", asin: "D1");
            CachedAuthor("Frank Herbert", Catalog("Dune Messiah", "Frank Herbert", asin: "D2"));
            _monitoredAuthors.Add(new MonitoredAuthor { AuthorName = "Frank  Herbert" });

            var snapshot = await BuildService().GetAsync();

            Assert.True(Assert.Single(snapshot.Authors).Monitored);
        }

        [Fact]
        public async Task Get_LeavesOutWhatWasIgnored_ByAsinOrByTitleAndAuthor()
        {
            // Translations and regional retitles are the usual reasons; both kinds of
            // identity must hold so a re-fetched catalog does not bring them back.
            Held("Dune", "Frank Herbert", asin: "D1");
            CachedAuthor("Frank Herbert",
                Catalog("Dune Messiah", "Frank Herbert", asin: "D2"),
                Catalog("Der Wüstenplanet", "Frank Herbert", asin: "D3"),
                Catalog("Children of Dune", "Frank Herbert"));
            var service = BuildService();

            await service.IgnoreAsync(new IgnoreSuggestionRequest("D3", "Der Wüstenplanet", ["Frank Herbert"]));
            await service.IgnoreAsync(new IgnoreSuggestionRequest(null, "Children of Dune: A Novel", ["Frank Herbert"]));
            var snapshot = await service.GetAsync();

            Assert.Equal("Dune Messiah", Assert.Single(Assert.Single(snapshot.Authors).Missing).Title);
            Assert.Equal(2, snapshot.Ignored.Count);
            Assert.Equal("asin:D3", snapshot.Ignored[0].Key);
        }

        [Fact]
        public async Task Get_ListsAuthorsWithMoreOfTheLibraryFirst()
        {
            Held("A", "Minor Author", asin: "M1");
            Held("B", "Major Author", asin: "J1");
            Held("C", "Major Author", asin: "J2");
            CachedAuthor("Minor Author", Catalog("A2", "Minor Author", asin: "M2"));
            CachedAuthor("Major Author", Catalog("D", "Major Author", asin: "J3"));

            var snapshot = await BuildService().GetAsync();

            Assert.Equal(["Major Author", "Minor Author"], snapshot.Authors.Select(group => group.Author));
        }
    }
}
