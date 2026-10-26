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
using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Suggestions
{
    /// <summary>
    /// A suggestion only stays on the page when an indexer holds a release that is that
    /// book, and adding it grabs that release. These pin what counts as that book.
    /// </summary>
    [Trait("Name", "SuggestionReleaseServiceTests")]
    [Trait("Category", "Application")]
    public sealed class SuggestionReleaseServiceTests : BaseTests
    {
        private static readonly Indexer Abook = new() { Id = 1, Name = "Abook.link", Implementation = "AbookLink" };
        private static readonly Indexer Geek = new() { Id = 2, Name = "NZBgeek", Implementation = "Newznab" };

        private readonly Mock<ISearchService> _search = new();
        private readonly Mock<IIndexerRepository> _indexers = new();
        private readonly Mock<IQualityProfileService> _profiles = new();
        private readonly Mock<IDownloadService> _downloads = new();
        private readonly Mock<IAbookLinkBrowser> _abook = new();
        private readonly Dictionary<string, SuggestionRelease> _stored = new(StringComparer.OrdinalIgnoreCase);

        private sealed class MarkingProtector : ISecretProtector
        {
            public string Protect(string plaintext) => "protected:" + plaintext;
            public string Unprotect(string protectedValue) => protectedValue["protected:".Length..];
        }

        private SuggestionReleaseService Build(params Indexer[] enabled)
        {
            _indexers.Setup(r => r.GetEnabledAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync(enabled.ToList());
            _profiles.Setup(p => p.GetDefaultAsync()).ReturnsAsync((QualityProfile?)null);
            var store = new Mock<ISuggestionReleaseRepository>();
            store.Setup(s => s.SaveAsync(It.IsAny<SuggestionRelease>(), It.IsAny<CancellationToken>()))
                .Callback<SuggestionRelease, CancellationToken>((release, _) => _stored[release.Asin] = release)
                .Returns(Task.CompletedTask);
            store.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string asin, CancellationToken _) => _stored.GetValueOrDefault(asin));
            _abook.Setup(a => a.GetTopicAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbookBrowseResult(false, 0, [], new AbookParseReport(), "not stubbed"));
            return new SuggestionReleaseService(
                _search.Object, _indexers.Object, _profiles.Object, new MarkingProtector(),
                store.Object, _downloads.Object, _abook.Object,
                NullLogger<SuggestionReleaseService>.Instance);
        }

        private void Returns(Indexer indexer, params IndexerSearchResult[] results) =>
            _search.Setup(s => s.SearchIndexerResultsAsync(
                    indexer.Id.ToString(), It.IsAny<string>(), null, It.IsAny<SearchRequest?>()))
                .ReturnsAsync(results.ToList());

        /// <summary>The post abook.link gives up when the topic behind a hit is opened.</summary>
        private void Topic(int topicId, AbookPost post) =>
            _abook.Setup(a => a.GetTopicAsync(topicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbookBrowseResult(
                    true, 1, [new AbookCandidate(topicId, post.Title ?? string.Empty, post)],
                    new AbookParseReport(), null));

        private static SuggestedBook Book(string asin, string title, string author) => new(
            asin, title, null, [author], [], null, null, "english", null, [], null, null, null, null, null, null,
            null, null, null, null);

        [Fact]
        public async Task AnAbookLinkPostIsFoundOnlyByTheBooksAsin()
        {
            Returns(Abook,
                new IndexerSearchResult { Id = "abook:1", Title = "Stanislaw Lem - Solaris (1970)", Asin = "B000OTHER0" },
                new IndexerSearchResult { Id = "abook:71604", Title = "Stanislaw Lem - Solaris: The Definitive Edition (2011)", Asin = "B0053ZT602" });
            var service = Build(Abook);

            var release = await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));

            Assert.Equal(SuggestionReleaseStatus.Found, release.Status);
            Assert.Equal("Abook.link", release.IndexerName);
            Assert.Contains("abook:71604", release.ProtectedResult);
            Assert.StartsWith("protected:", release.ProtectedResult);
        }

        [Fact]
        public async Task AnAbookLinkPostWithTheRightTitleButAnotherAsinIsNotThisBook()
        {
            Returns(Abook, new IndexerSearchResult { Id = "abook:1", Title = "Stanislaw Lem - Solaris (1970)", Asin = "B000OTHER0" });
            Returns(Geek);
            var service = Build(Abook, Geek);

            var release = await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));

            Assert.Equal(SuggestionReleaseStatus.NotFound, release.Status);
            Assert.Null(release.ProtectedResult);
        }

        [Fact]
        public async Task AnAbookLinkPostWithNoAsinIsMatchedByItsParsedTitleAndAuthor()
        {
            // Most posts, older ones especially, declare no ASIN.
            Returns(Abook,
                new IndexerSearchResult { Id = "abook:1", Album = "Breakaway Point", Artist = "Craig Alanson" },
                new IndexerSearchResult { Id = "abook:2", Album = "Breakaway", Artist = "Someone Else" },
                new IndexerSearchResult { Id = "abook:74590", Album = "Breakaway", Artist = "Craig Alanson", Title = "Craig Alanson - Expeditionary Force 12 - Breakaway (2021) [M4B]" });
            var service = Build(Abook);

            var release = await service.CheckAsync(Book("B08XYZ1234", "Breakaway", "Craig Alanson"));

            Assert.Equal(SuggestionReleaseStatus.Found, release.Status);
            Assert.Contains("abook:74590", release.ProtectedResult);
            Assert.Equal("The post's title and author match.", release.Detail);
        }

        [Fact]
        public async Task ANewznabReleaseMustCarryTheTitleAndTheAuthorsSurname()
        {
            Returns(Abook);
            Returns(Geek,
                new IndexerSearchResult { Id = "a", Title = "Solaris Rising 3 - Ian Whates", Size = 900 },
                new IndexerSearchResult { Id = "b", Title = "Stanislaw.Lem.Solaris.Definitive.Edition.MP3", Size = 200, NzbUrl = "https://geek/b?apikey=x" });
            var service = Build(Abook, Geek);

            var release = await service.CheckAsync(Book("B0053ZT602", "Solaris: The Definitive Edition", "Stanislaw Lem"));

            Assert.Equal(SuggestionReleaseStatus.Found, release.Status);
            Assert.Equal("NZBgeek", release.IndexerName);
            Assert.Equal("Stanislaw.Lem.Solaris.Definitive.Edition.MP3", release.ReleaseTitle);
        }

        [Fact]
        public async Task AbookLinkIsAskedFirstAndAnAnswerThereSparesTheNewznabIndexers()
        {
            Returns(Abook, new IndexerSearchResult { Id = "abook:71604", Title = "Solaris", Asin = "B0053ZT602" });
            var service = Build(Geek, Abook);

            await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));

            _search.Verify(s => s.SearchIndexerResultsAsync(
                "2", It.IsAny<string>(), null, It.IsAny<SearchRequest?>()), Times.Never);
        }

        [Fact]
        public async Task AddingAFoundBookSendsTheStoredReleaseWithoutSearching()
        {
            Returns(Abook, new IndexerSearchResult
            {
                Id = "abook:71604",
                Title = "Solaris",
                Asin = "B0053ZT602",
                DownloadType = "Usenet",
                IndexerId = 1,
                IndexerImplementation = "AbookLink"
            });
            var service = Build(Abook);
            await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));
            _search.Invocations.Clear();
            _downloads.Setup(d => d.SendToDownloadClientAsync(It.IsAny<TrustedDownloadCandidate>(), null, 1273))
                .ReturnsAsync("download-1");

            var grabbed = await service.GrabStoredAsync(new Audiobook { Id = 1273, Asin = "B0053ZT602", Title = "Solaris" });

            Assert.True(grabbed);
            _downloads.Verify(d => d.SendToDownloadClientAsync(
                It.Is<TrustedDownloadCandidate>(c => c.Id == "abook:71604"), null, 1273), Times.Once);
            _search.VerifyNoOtherCalls();
        }

        /// <summary>
        /// "Nobody has it" is a claim about the indexers' catalogs, and only an indexer
        /// that answered can make it. When they all fail the honest answer is that nothing
        /// was learned - otherwise a bad night at the indexer reads as a missing book.
        /// </summary>
        [Fact]
        public async Task EveryIndexerFailingIsNotTheSameAsNobodyHavingTheBook()
        {
            _search.Setup(s => s.SearchIndexerResultsAsync(
                    It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<SearchRequest?>()))
                .ThrowsAsync(new HttpRequestException("indexer is down"));
            var service = Build(Abook, Geek);

            var release = await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));

            Assert.Equal(SuggestionReleaseStatus.Unknown, release.Status);
            Assert.Contains("could be reached", release.Detail);
        }

        [Fact]
        public async Task AnIndexerThatAnsweredCanStillReportNobodyHasIt()
        {
            Returns(Abook);
            _search.Setup(s => s.SearchIndexerResultsAsync(
                    Geek.Id.ToString(), It.IsAny<string>(), null, It.IsAny<SearchRequest?>()))
                .ThrowsAsync(new HttpRequestException("indexer is down"));
            var service = Build(Abook, Geek);

            var release = await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));

            Assert.Equal(SuggestionReleaseStatus.NotFound, release.Status);
            // Only the indexer that actually answered may be named as having looked.
            Assert.Contains("Abook.link", release.Detail);
            Assert.DoesNotContain("NZBgeek", release.Detail);
        }

        [Fact]
        public async Task AddingABookNothingHeldSendsNothing()
        {
            Returns(Abook);
            var service = Build(Abook);
            await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));

            Assert.False(await service.GrabStoredAsync(new Audiobook { Id = 1273, Asin = "B0053ZT602" }));
            _downloads.VerifyNoOtherCalls();
        }
    
        [Fact]
        public async Task TheCheckDoesNotPayToOpenEveryHitAbookLinkAnswersWith()
        {
            SearchRequest? asked = null;
            _search.Setup(s => s.SearchIndexerResultsAsync(
                    Abook.Id.ToString(), It.IsAny<string>(), null, It.IsAny<SearchRequest?>()))
                .Callback<string, string, string?, SearchRequest?>((_, _, _, request) => asked = request)
                .ReturnsAsync([]);
            var service = Build(Abook);

            await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));

            // The hits' titles say which posts could be the book. Reading fifteen of them
            // to find that out is fifteen requests to the forum for every book checked.
            Assert.NotNull(asked);
            Assert.False(asked!.IncludeEnrichment);
        }

        [Fact]
        public async Task NoHitWorthReadingMeansNoTopicIsOpened()
        {
            Returns(Abook, new IndexerSearchResult
            {
                Id = "abook:1",
                Title = "Iain M. Banks - Culture 01 - Consider Phlebas (1987)",
                Album = "Consider Phlebas",
                Artist = "Iain M. Banks",
                DownloadReference = "1"
            });
            var service = Build(Abook);

            var release = await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));

            Assert.Equal(SuggestionReleaseStatus.NotFound, release.Status);
            _abook.Verify(a => a.GetTopicAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task APostThatNamesAnotherAsinIsRejectedOnceItsOwnPostIsRead()
        {
            // The title alone cannot tell these apart - the ASIN is only in the post, and a
            // post that declares one is held to it. Shortlisting by title and confirming by
            // post keeps that rule without opening every hit.
            Returns(Abook, new IndexerSearchResult
            {
                Id = "abook:71604",
                Title = "Stanislaw Lem - Solaris (2011)",
                Album = "Solaris",
                Artist = "Stanislaw Lem",
                DownloadReference = "71604"
            });
            var service = Build(Abook);
            Topic(71604, new AbookPost
            {
                Title = "Solaris",
                Author = "Stanislaw Lem",
                Asin = "B000OTHER0",
                Format = "M4B",
                SizeBytes = 300_000_000
            });

            var release = await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));

            Assert.Equal(SuggestionReleaseStatus.NotFound, release.Status);
            _abook.Verify(a => a.GetTopicAsync(71604, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ReadingTheShortlistedPostGivesTheReleaseItsSizeAndFormat()
        {
            Returns(Abook, new IndexerSearchResult
            {
                Id = "abook:71604",
                Title = "Stanislaw Lem - Solaris (2011)",
                Album = "Solaris",
                Artist = "Stanislaw Lem",
                DownloadReference = "71604"
            });
            var service = Build(Abook);
            Topic(71604, new AbookPost
            {
                Title = "Solaris",
                Author = "Stanislaw Lem",
                Asin = "B0053ZT602",
                Format = "M4B",
                SizeBytes = 300_000_000
            });

            var release = await service.CheckAsync(Book("B0053ZT602", "Solaris", "Stanislaw Lem"));

            Assert.Equal(SuggestionReleaseStatus.Found, release.Status);
            Assert.Equal(300_000_000, release.SizeBytes);
        }
    
        [Fact]
        public async Task ATitleLineTheParserMissplitsIsStillWorthReading()
        {
            // "SERRAted Edge 10 - Breaking Silence by Mercedes Lackey, Cody Martin" gives
            // the parser an author of "SERRAted Edge 10", so no parsed field says which
            // book it is - but the line does, and the post behind it settles the matter.
            Returns(Abook, new IndexerSearchResult
            {
                Id = "abook:4210",
                Title = "SERRAted Edge 10 - Breaking Silence by Mercedes Lackey, Cody Martin",
                Album = "Breaking Silence by Mercedes Lackey, Cody Martin",
                Artist = "SERRAted Edge 10",
                DownloadReference = "4210"
            });
            var service = Build(Abook);
            Topic(4210, new AbookPost
            {
                Title = "Breaking Silence",
                Author = "Mercedes Lackey, Cody Martin",
                Asin = "B07MFJLBQR",
                Format = "M4B",
                SizeBytes = 250_000_000
            });

            var release = await service.CheckAsync(
                Book("B07MFJLBQR", "Breaking Silence", "Mercedes Lackey"));

            Assert.Equal(SuggestionReleaseStatus.Found, release.Status);
            _abook.Verify(a => a.GetTopicAsync(4210, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
