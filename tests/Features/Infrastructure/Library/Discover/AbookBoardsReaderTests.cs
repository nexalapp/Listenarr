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
using Listenarr.Application.Search.AbookLink;
using Listenarr.Infrastructure.Library.Discover;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Discover
{
    /// <summary>
    /// What a board listing becomes: a row per release, and nothing for the posts that
    /// are not one. Nothing here opens a topic or reveals a payload.
    /// </summary>
    [Trait("Name", "AbookBoardsReaderTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AbookBoardsReaderTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 10, 24, 3, 25, 0, DateTimeKind.Utc);

        private readonly Mock<IAbookLinkBrowser> _browser = new();
        private readonly Mock<IIndexerRepository> _indexers = new();
        private readonly Mock<ISecretProtector> _secrets = new();
        private readonly List<Indexer> _configured = [];
        private readonly List<AbookTopicListing> _topics = [];

        private AbookBoardsReader BuildReader()
        {
            _indexers.Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_configured);
            _secrets.Setup(protector => protector.Unprotect(It.IsAny<string>()))
                .Returns<string>(value => value);
            _browser.Setup(browser => browser.BoardTopicsAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new AbookListingResult<AbookTopicListing>(true, _topics, null));

            return new AbookBoardsReader(
                _browser.Object,
                _indexers.Object,
                _secrets.Object,
                NullLogger<AbookBoardsReader>.Instance,
                new FixedClock(Now));
        }

        [Fact]
        public async Task UnavailableReason_WithNoAbookSourceConfigured_SaysWhereToAddOne()
        {
            var reason = await BuildReader().UnavailableReasonAsync();

            Assert.Contains("Settings", reason);
        }

        [Fact]
        public async Task UnavailableReason_WithOneConfigured_IsNone()
        {
            Configured();

            Assert.Null(await BuildReader().UnavailableReasonAsync());
        }

        [Fact]
        public async Task GetPage_TurnsATopicTitleIntoABookWorthShowing()
        {
            Configured();
            _topics.Add(Listing(123463, "Jack Campbell - Lost Fleet 04 - Valiant (2022)", Now.AddHours(-1)));

            var page = await BuildReader().GetPageAsync(Section(), 1);

            var item = Assert.Single(page.Items);
            Assert.Equal("123463", item.ExternalId);
            Assert.Equal("Valiant", item.Book.Title);
            Assert.Equal("Jack Campbell", Assert.Single(item.Book.Authors));
            Assert.Equal("Lost Fleet", item.Book.Series);
            Assert.Equal("04", item.Book.SeriesNumber);
            Assert.Equal("2022", item.Book.PublishedDate);
            Assert.Equal("https://abook.link/book/index.php?topic=123463", item.SourceUrl);

            // The poster's own line says what no catalogue does - the bitrate, the
            // narration, which rip this is - so it is kept verbatim.
            Assert.Equal("Jack Campbell - Lost Fleet 04 - Valiant (2022)", item.SourceNote);

            // Nothing has been looked up: the cover and the ASIN come later.
            Assert.False(item.Resolved);
        }

        [Theory]
        [InlineData("[REQUEST] Anything by Adrian Tchaikovsky")]
        [InlineData("[FILLED] Adrian Tchaikovsky - Children of Time")]
        [InlineData("[Reading Order] The Expanse")]
        [InlineData("[SPOT] Larry Niven - Ringworld (1970)")]
        public async Task GetPage_LeavesOutThePostsThatAreNotAReleaseBecomingAvailable(string subject)
        {
            Configured();
            _topics.Add(Listing(1, subject, Now.AddHours(-1)));

            Assert.Empty((await BuildReader().GetPageAsync(Section(), 1)).Items);
        }

        [Fact]
        public async Task GetPage_DoesNotDateAPostInTheFuture()
        {
            Configured();
            // The forum prints its times in the signed-in account's offset. On an
            // account set ahead of UTC, today's posts read as hours from now.
            _topics.Add(Listing(1, "Jack Campbell - Lost Fleet 04 - Valiant (2022)", Now.AddHours(9)));

            var item = Assert.Single((await BuildReader().GetPageAsync(Section(), 1)).Items);

            Assert.Equal(Now, item.PostedAt);
        }

        [Fact]
        public async Task GetPage_KeepsATimeThatHasAlreadyHappened()
        {
            Configured();
            _topics.Add(Listing(1, "Jack Campbell - Lost Fleet 04 - Valiant (2022)", Now.AddDays(-2)));

            var item = Assert.Single((await BuildReader().GetPageAsync(Section(), 1)).Items);

            Assert.Equal(Now.AddDays(-2), item.PostedAt);
        }

        [Fact]
        public async Task GetPage_WithAnEmptyPage_SaysThereIsNoMore()
        {
            Configured();

            var page = await BuildReader().GetPageAsync(Section(), 9);

            Assert.True(page.Succeeded);
            Assert.False(page.HasMore);
        }

        [Fact]
        public async Task GetPage_WhenTheForumCannotBeRead_SaysWhy()
        {
            Configured();
            var reader = BuildReader();
            _browser.Setup(browser => browser.BoardTopicsAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AbookListingResult<AbookTopicListing>.Failed("The abook.link session has expired."));

            var page = await reader.GetPageAsync(Section(), 1);

            Assert.False(page.Succeeded);
            Assert.Equal("The abook.link session has expired.", page.Reason);
        }

        private static DiscoverSelectedSection Section() => new("7", "Science Fiction");

        private static AbookTopicListing Listing(int topicId, string subject, DateTime postedAt) =>
            new(topicId, subject, "7", null, 0, postedAt);

        private sealed class FixedClock(DateTime utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        }

        private void Configured() =>
            _configured.Add(new Indexer
            {
                Id = 1,
                Name = "abook.link",
                AdditionalSettings = """{"abook_session_cookie":"PHPSESSID=test"}"""
            });
    }
}
