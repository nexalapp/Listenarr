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
using Listenarr.Domain.Audiobooks.Discover;
using Listenarr.Infrastructure.Library.Discover;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Discover
{
    /// <summary>
    /// Which catalogue hit is the book a forum post named.
    ///
    /// <para>
    /// Getting this wrong is invisible: the row shows a cover, and the cover is of
    /// another book. So the author must agree, and a title only counts when the
    /// catalogue's own title contains the post's, whole. The cases here are the ones
    /// the live boards produced.
    /// </para>
    /// </summary>
    [Trait("Name", "AudibleDiscoverMetadataResolverTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AudibleDiscoverMetadataResolverTests : BaseTests
    {
        private readonly Mock<AudibleService> _audible;
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly ApplicationSettings _settings = new();
        private readonly List<AudibleSearchResult> _keywordHits = [];
        private readonly List<AudibleSearchResult> _authorCatalog = [];

        public AudibleDiscoverMetadataResolverTests()
        {
            _audible = new Mock<AudibleService>(
                new HttpClient(), NullLogger<AudibleService>.Instance)
            { CallBase = false };
        }

        private AudibleDiscoverMetadataResolver BuildResolver()
        {
            _configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(_settings);
            _audible
                .Setup(a => a.SearchBooksAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(() => new AudibleSearchResponse { Results = _keywordHits });
            _audible
                .Setup(a => a.SearchByTitleAndAuthorAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(() => new AudibleSearchResponse { Results = _authorCatalog });

            return new AudibleDiscoverMetadataResolver(
                _audible.Object,
                _configuration.Object,
                NullLogger<AudibleDiscoverMetadataResolver>.Instance);
        }

        [Fact]
        public async Task Resolve_TakesTheBookWhoseTitleMatchesExactly()
        {
            _keywordHits.Add(Hit("B1", "Valiant", "Jack Campbell"));

            var book = await BuildResolver().ResolveAsync(Post("Valiant", "Jack Campbell"));

            Assert.Equal("B1", book?.Asin);
        }

        [Fact]
        public async Task Resolve_TakesABookTheCatalogueSellsUnderItsSeriesName()
        {
            // The forum keeps the series in its own field: "Jack Campbell - Lost Fleet
            // 04 - Valiant". Audible sells the same book as "The Lost Fleet: Valiant".
            _keywordHits.Add(Hit("B1", "The Lost Fleet: Valiant", "Jack Campbell"));

            var book = await BuildResolver().ResolveAsync(Post("Valiant", "Jack Campbell"));

            Assert.Equal("B1", book?.Asin);
        }

        [Fact]
        public async Task Resolve_PrefersTheExactTitleOverOneThatMerelyContainsIt()
        {
            _keywordHits.Add(Hit("B1", "The Lost Fleet: Valiant", "Jack Campbell"));
            _keywordHits.Add(Hit("B2", "Valiant", "Jack Campbell"));

            var book = await BuildResolver().ResolveAsync(Post("Valiant", "Jack Campbell"));

            Assert.Equal("B2", book?.Asin);
        }

        [Fact]
        public async Task Resolve_PrefersTheHitInTheSeriesThePostNamed()
        {
            _keywordHits.Add(Hit("B1", "Valiant", "Jack Campbell"));
            _keywordHits.Add(Hit("B2", "Valiant", "Jack Campbell", series: "Lost Fleet"));

            var book = await BuildResolver().ResolveAsync(
                Post("Valiant", "Jack Campbell", series: "Lost Fleet"));

            Assert.Equal("B2", book?.Asin);
        }

        [Fact]
        public async Task Resolve_RefusesABookOfTheSameNameBySomebodyElse()
        {
            _keywordHits.Add(Hit("B1", "Valiant", "Sarah J. Maas"));

            Assert.Null(await BuildResolver().ResolveAsync(Post("Valiant", "Jack Campbell")));
        }

        [Fact]
        public async Task Resolve_RefusesATitleThatOnlyStartsTheSame()
        {
            _keywordHits.Add(Hit("B1", "Driven", "James S. A. Corey"));

            // "Drive" is not "Driven", however close the letters are.
            Assert.Null(await BuildResolver().ResolveAsync(Post("Drive", "James S. A. Corey")));
        }

        [Fact]
        public async Task Resolve_RefusesAOneWordTitleTooShortToMeanAnything()
        {
            _keywordHits.Add(Hit("B1", "The Long Way to a Small Angry Planet", "Becky Chambers"));

            // "Way" appearing somewhere in a longer title is not a match.
            Assert.Null(await BuildResolver().ResolveAsync(Post("Way", "Becky Chambers")));
        }

        [Fact]
        public async Task Resolve_MatchesAnAuthorTheForumAbbreviated()
        {
            _keywordHits.Add(Hit("B1", "Nemesis Games", "James S. A. Corey"));

            var book = await BuildResolver().ResolveAsync(Post("Nemesis Games", "J. S. A. Corey"));

            // One shared name part of real length is enough; initials are not.
            Assert.Equal("B1", book?.Asin);
        }

        [Fact]
        public async Task Resolve_FallsBackToTheAuthorsCatalogueWhenKeywordsFindNothing()
        {
            _authorCatalog.Add(Hit("B1", "The Lost Regiment: Men of War", "William R. Forstchen"));

            var book = await BuildResolver().ResolveAsync(Post("Men of War", "William R. Forstchen"));

            Assert.Equal("B1", book?.Asin);
            _audible.Verify(
                a => a.SearchByTitleAndAuthorAsync(
                    "Men of War",
                    "William R. Forstchen",
                    1,
                    50,
                    It.IsAny<string>(),
                    It.IsAny<string?>()),
                Times.Once);
        }

        [Fact]
        public async Task Resolve_DoesNotAskTheCatalogueTwiceWhenTheKeywordsAnswered()
        {
            _keywordHits.Add(Hit("B1", "Valiant", "Jack Campbell"));

            await BuildResolver().ResolveAsync(Post("Valiant", "Jack Campbell"));

            _audible.Verify(
                a => a.SearchByTitleAndAuthorAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>()),
                Times.Never);
        }

        [Fact]
        public async Task Resolve_WithAudibleTurnedOff_LooksNothingUp()
        {
            _settings.EnableAudibleSearch = false;
            _keywordHits.Add(Hit("B1", "Valiant", "Jack Campbell"));

            Assert.Null(await BuildResolver().ResolveAsync(Post("Valiant", "Jack Campbell")));
            _audible.Verify(
                a => a.SearchBooksAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()),
                Times.Never);
        }

        [Fact]
        public async Task Resolve_OfAPostWithNoTitle_LooksNothingUp()
        {
            Assert.Null(await BuildResolver().ResolveAsync(new DiscoverBook { Title = "  " }));
        }

        private static DiscoverBook Post(string title, string author, string? series = null) =>
            new()
            {
                Title = title,
                Authors = [author],
                Series = series
            };

        private static AudibleSearchResult Hit(string asin, string title, string author, string? series = null) =>
            new()
            {
                Asin = asin,
                Title = title,
                Authors = [new AudibleAuthor { Name = author }],
                Series = series is null ? null : [new AudibleSeries { Name = series }],
                ImageUrl = $"https://example.invalid/{asin}.jpg"
            };
    }
}
