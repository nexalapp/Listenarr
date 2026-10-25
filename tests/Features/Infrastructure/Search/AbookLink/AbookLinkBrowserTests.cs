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
using System.Net;
using Listenarr.Application.Search.AbookLink;
using Listenarr.Infrastructure.Search.AbookLink;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.AbookLink
{
    /// <summary>
    /// How much of a search's answer is considered.
    ///
    /// <para>
    /// The forum's search tool returns every hit on one page — fifty of them for a
    /// common author — while opening a topic costs a request each. Judging only the
    /// opened ones meant a book at position sixteen was reported as not existing, which
    /// is how 1,947 of 2,875 suggestions came to be marked as having no release.
    /// </para>
    /// </summary>
    [Trait("Name", "AbookLinkBrowserTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AbookLinkBrowserTests : BaseTests
    {
        private readonly List<string> _requested = [];

        /// <summary>A results page in the shape the search tool serves, with N hits.</summary>
        private static string SearchPage(params (int TopicId, string Title)[] hits)
        {
            var rows = string.Join("\n", hits.Select(hit =>
                $"<a href='https://abook.link/book/index.php?topic={hit.TopicId}&r=4' target='_blank'>{hit.Title}</a><br/>"));
            return $"""
                <div class="layout_box"><div class="search_results">
                <div class='page_nav'><span>{hits.Length} Results</span> - Pages: [1]</div>
                {rows}
                </div></div>
                """;
        }

        private static string TopicPage(string title, string nfo) =>
            $"""
            <html><head><title>Science Fiction - {title}</title></head><body>
            <div id="msg_900"><div class="post">{nfo.Replace("\n", "<br />")}</div></div>
            </body></html>
            """;

        private AbookLinkBrowser BuildBrowser(string searchPage, Func<int, string> topicPage)
        {
            var handler = new DelegatingHandlerMock((request, _) =>
            {
                var url = request.RequestUri!.ToString();
                _requested.Add(url);
                var topic = System.Text.RegularExpressions.Regex.Match(url, @"topic=(\d+)");
                var body = topic.Success
                    ? topicPage(int.Parse(topic.Groups[1].Value))
                    : searchPage;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body)
                });
            });

            var indexers = new Mock<IIndexerRepository>();
            indexers.Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new Indexer
                    {
                        Id = 1,
                        Name = "Abook.link",
                        Implementation = "AbookLink",
                        AdditionalSettings = """{"abook_session_cookie":"PHPSESSID=test"}"""
                    }
                ]);

            var secrets = new Mock<ISecretProtector>();
            secrets.Setup(protector => protector.Unprotect(It.IsAny<string>())).Returns<string>(value => value);

            return new AbookLinkBrowser(
                new AbookLinkClient(new HttpClient(handler), NullLogger<AbookLinkClient>.Instance),
                new AbookLinkSession(new HttpClient(handler), NullLogger<AbookLinkSession>.Instance),
                indexers.Object,
                secrets.Object,
                new Mock<INzbKingTokenBudget>().Object,
                new Mock<INzbKingLedgerRepository>().Object,
                NullLogger<AbookLinkBrowser>.Instance);
        }

        private const string Nfo = """
            Author: Jack Campbell
            Title: Valiant
            Series: Lost Fleet
            Book Number: 04
            Media Format: M4B
            Total Size: 310 MB
            Search: deadbeefdeadbeefdeadbeefdeadbeef
            """;

        [Fact]
        public async Task Search_KeepsEveryHit_NotOnlyTheOnesItOpened()
        {
            var hits = Enumerable.Range(1, 20)
                .Select(n => (n, $"Jack Campbell - Lost Fleet {n:00} - Book {n} (2022)"))
                .ToArray();
            var browser = BuildBrowser(SearchPage(hits), _ => TopicPage("Valiant", Nfo));

            var result = await browser.SearchAsync("lost fleet", inspect: 3);

            Assert.True(result.Succeeded);
            Assert.Equal(20, result.HitCount);
            Assert.Equal(20, result.Candidates.Count);
        }

        [Fact]
        public async Task Search_OpensOnlyAsManyTopicsAsItWasToldTo()
        {
            var hits = Enumerable.Range(1, 20)
                .Select(n => (n, $"Jack Campbell - Lost Fleet {n:00} - Book {n} (2022)"))
                .ToArray();
            var browser = BuildBrowser(SearchPage(hits), _ => TopicPage("Valiant", Nfo));

            await browser.SearchAsync("lost fleet", inspect: 3);

            // One search plus three topics. The other seventeen are read from their
            // title line: this is somebody's forum, not an API.
            Assert.Equal(3, _requested.Count(url => url.Contains("topic=", StringComparison.Ordinal)));
            Assert.Single(_requested, url => url.Contains("search_abook.php", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Search_IdentifiesAnUnopenedHitFromItsTitleLine()
        {
            var browser = BuildBrowser(
                SearchPage(
                    (1, "Jack Campbell - Lost Fleet 01 - Dauntless (2022)"),
                    (2, "Mark Tufo - Zombie Fallout 13 - Etna Station (2019)")),
                _ => TopicPage("Dauntless", Nfo));

            var result = await browser.SearchAsync("etna station", inspect: 1);

            var unopened = result.Candidates.Single(candidate => candidate.TopicId == 2);
            Assert.Equal("Etna Station", unopened.Post.Title);
            Assert.Equal("Mark Tufo", unopened.Post.Author);
            Assert.Equal("Zombie Fallout", unopened.Post.SeriesName);
            Assert.Equal("13", unopened.Post.SeriesPosition);
            Assert.Equal(2019, unopened.Post.Year);
            Assert.True(unopened.Post.HasIdentity);

            // The payload is behind the thanks and is read at grab time, so the most an
            // unopened hit can be is "identified, no search string".
            Assert.Equal(AbookParseOutcome.MissingSearchString, unopened.Post.Outcome);
        }

        [Fact]
        public async Task Search_StillOpensTheFirstHitsForTheirNfo()
        {
            var browser = BuildBrowser(
                SearchPage(
                    (1, "Jack Campbell - Lost Fleet 04 - Valiant (2022)"),
                    (2, "Jack Campbell - Lost Fleet 05 - Relentless (2022)")),
                _ => TopicPage("Valiant", Nfo));

            var result = await browser.SearchAsync("valiant", inspect: 1);

            var opened = result.Candidates.Single(candidate => candidate.TopicId == 1);

            // The format and size are in the NFO and nowhere else; a title line cannot
            // give them, which is why the first hits are still opened. (What the payload
            // block looks like is pinned against real posts in the parser's own fixtures.)
            Assert.Equal("M4B", opened.Post.Format);
            Assert.True(opened.Post.HasIdentity);
            Assert.Null(result.Candidates.Single(candidate => candidate.TopicId == 2).Post.Format);
        }

        [Fact]
        public async Task Search_LeavesTheNonReleasesRecognisableAsSuch()
        {
            var browser = BuildBrowser(
                SearchPage(
                    (1, "Jack Campbell - Lost Fleet 04 - Valiant (2022)"),
                    (2, "[REQUEST] Anything by Mark Tufo")),
                _ => TopicPage("Valiant", Nfo));

            var result = await browser.SearchAsync("tufo", inspect: 1);

            var request = result.Candidates.Single(candidate => candidate.TopicId == 2);
            Assert.Equal(AbookParseOutcome.NotARelease, request.Post.Outcome);
        }
    }
}
