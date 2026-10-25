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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Metadata.Audible
{
    /// <summary>
    /// The chart page is markup we do not control, so these pin the two things taken
    /// off it against the shapes the live page actually uses — including the ones that
    /// would quietly produce a wrong answer rather than no answer.
    /// </summary>
    [Trait("Name", "AudibleChartsParserTests")]
    [Trait("Category", "Application")]
    public sealed class AudibleChartsParserTests : BaseTests
    {
        /// <summary>
        /// Trimmed from the live page. The subscription product and the bare
        /// <c>asin=</c> attributes outside the list are there on purpose: they are on
        /// the real page, and reading one as a chart entry would offer "Audible
        /// Standard" as a book to add.
        /// </summary>
        private const string ChartPage = """
            <html><body>
              <div data-asin="B0DBVXZZT9" class="adbl-banner">Audible Standard</div>
              <span asin="B0DBVXZZT9"></span>
              <span class="refinementFormLink">21 - 40 of 100 results</span>
              <ul class="bc-list bc-list-nostyle">
                <li class="bc-list-item productListItem" id="product-list-item-B08V8B2CGV"
                    aria-label='Dungeon Crawler Carl'>
                  <a href="/pd/Dungeon-Crawler-Carl-Audiobook/B08V8B2CGV?ref=x">Dungeon Crawler Carl</a>
                </li>
                <li class="bc-list-item productListItem" id="product-list-item-B0GR6QBFPK"
                    aria-label='The Second'>
                  <a href="/pd/Second-Audiobook/B0GR6QBFPK">The Second</a>
                </li>
                <li class="bc-list-item productListItem" id="product-list-item-B0934GTSGT"
                    aria-label='The Third'>
                  <a href="/pd/Third-Audiobook/B0934GTSGT">The Third</a>
                </li>
              </ul>
            </body></html>
            """;

        /// <summary>
        /// The category filter, with the two shapes that break a naive read: a name
        /// holding an apostrophe, which ends the single-quoted aria-label early, and
        /// refinement links back to the same chart carrying a query string.
        /// </summary>
        private const string CategoryList = """
            <ul class="bc-list">
              <li class="bc-list-item">
                <a class="bc-link refinementFormRadio ch-link" href="/charts/best">
                  <input type="radio" value="all" name="setId" aria-label='All categories'/>
                  <span class="bc-radio-label bc-size-callout"> All categories </span>
                </a>
              </li>
              <li class="bc-list-item">
                <a class="bc-link refinementFormRadio ch-link" href="/charts/best/childrens-audiobooks-audiobooks/18572091011">
                  <input type="radio" value="18572091011" name="setId" aria-label='Children's Audiobooks'/>
                  <span class="bc-radio-label bc-size-callout"> Children's Audiobooks </span>
                </a>
              </li>
              <li class="bc-list-item">
                <a class="bc-link refinementFormRadio ch-link" href="/charts/best/science-fiction-fantasy-audiobooks/18580606011">
                  <input type="radio" value="18580606011" name="setId" aria-label='Science Fiction &amp; Fantasy'/>
                  <span class="bc-radio-label bc-size-callout"> Science Fiction &amp; Fantasy </span>
                </a>
              </li>
              <li class="bc-list-item">
                <a class="bc-link" href="/charts/best/science-fiction-fantasy-audiobooks/18580606011?searchLanguage=german">
                  <span class="bc-radio-label bc-size-callout"> German </span>
                </a>
              </li>
            </ul>
            """;

        [Fact]
        public void ParsePage_TakesTheChartEntriesInRankOrder()
        {
            var page = AudibleChartsParser.ParsePage(ChartPage);

            Assert.Equal(["B08V8B2CGV", "B0GR6QBFPK", "B0934GTSGT"], page.Asins);
        }

        [Fact]
        public void ParsePage_IgnoresProductsThatAreNotChartEntries()
        {
            var page = AudibleChartsParser.ParsePage(ChartPage);

            // The subscription banner carries an ASIN and is not a book in the chart.
            Assert.DoesNotContain("B0DBVXZZT9", page.Asins);
        }

        [Fact]
        public void ParsePage_ReadsHowFarThroughTheChartThePageIs()
        {
            var page = AudibleChartsParser.ParsePage(ChartPage);

            Assert.Equal(100, page.Total);
            Assert.Equal(40, page.Last);
        }

        [Fact]
        public void ParsePage_WithNothingToRead_SaysSoRatherThanGuessing()
        {
            var page = AudibleChartsParser.ParsePage("<html><body>nothing here</body></html>");

            Assert.Empty(page.Asins);
            Assert.Null(page.Total);
        }

        [Fact]
        public void ParseCategories_ReadsEveryChartTheFilterOffers()
        {
            var categories = AudibleChartsParser.ParseCategories(CategoryList);

            Assert.Equal(2, categories.Count);
            Assert.Equal("18572091011", categories[0].NodeId);
            Assert.Equal("childrens-audiobooks-audiobooks", categories[0].Slug);
            Assert.Equal("18580606011", categories[1].NodeId);
        }

        [Fact]
        public void ParseCategories_KeepsAnApostropheInTheName()
        {
            var categories = AudibleChartsParser.ParseCategories(CategoryList);

            // The aria-label beside it is single-quoted and ends at the apostrophe,
            // which would store this category as "Children".
            Assert.Equal("Children's Audiobooks", categories[0].Name);
        }

        [Fact]
        public void ParseCategories_DecodesEntitiesInTheName()
        {
            var categories = AudibleChartsParser.ParseCategories(CategoryList);

            Assert.Equal("Science Fiction & Fantasy", categories[1].Name);
        }

        [Fact]
        public void ParseCategories_LeavesOutTheOtherRefinements()
        {
            var categories = AudibleChartsParser.ParseCategories(CategoryList);

            // "All categories" has no node of its own, and the language links point
            // back at a chart already listed.
            Assert.DoesNotContain(categories, category => category.Name == "All categories");
            Assert.DoesNotContain(categories, category => category.Name == "German");
        }

        [Fact]
        public void ParseCategories_WithNoLabel_NamesTheCategoryFromItsUrl()
        {
            const string html =
                """<a href="/charts/best/science-fiction-fantasy-audiobooks/18580606011"><em></em></a>""";

            var categories = AudibleChartsParser.ParseCategories(html);

            Assert.Equal("Science Fiction Fantasy", Assert.Single(categories).Name);
        }
    }
}
