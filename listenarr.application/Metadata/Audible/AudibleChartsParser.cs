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
using System.Text.RegularExpressions;

namespace Listenarr.Application.Metadata.Audible
{
    /// <summary>A category Audible's charts can be filtered to.</summary>
    /// <param name="NodeId">Audible's browse-node id, which addresses the chart.</param>
    /// <param name="Slug">The URL segment that goes with it.</param>
    /// <param name="Name"></param>
    public sealed record AudibleChartCategory(string NodeId, string Slug, string Name);

    /// <summary>One page of a chart: the books it listed, in the order it listed them.</summary>
    /// <param name="Asins"></param>
    /// <param name="Total">
    /// What the page says the whole chart holds — "21 - 40 of 100 results". Audible's
    /// charts stop at 100, and a page past the end repeats the last one, so this is what
    /// tells a fetcher to stop rather than read the same twenty books forever.
    /// </param>
    /// <param name="Last"></param>
    public sealed record AudibleChartPage(IReadOnlyList<string> Asins, int? Total, int? Last);

    /// <summary>
    /// Reads an Audible charts page.
    ///
    /// <para>
    /// The page is server-rendered and the two things worth taking off it are marked
    /// plainly: every chart entry is a list item whose id is
    /// <c>product-list-item-&lt;ASIN&gt;</c>, in rank order, and the category filter is a
    /// list of links to other charts. Everything else about the book — title, narrator,
    /// cover, rating — is fetched from the catalog API by ASIN afterwards, which is both
    /// less brittle than scraping it and the same record the rest of the application
    /// already speaks.
    /// </para>
    /// <para>
    /// A product id is taken only from the list item, never from a bare <c>asin=</c>
    /// anywhere on the page: the page also carries Audible's own subscription products,
    /// and one of those read as a chart entry would be offered as a book to add.
    /// </para>
    /// </summary>
    public static partial class AudibleChartsParser
    {
        [GeneratedRegex(@"id=""product-list-item-([A-Z0-9]{10})""", RegexOptions.IgnoreCase)]
        private static partial Regex ProductListItem();

        [GeneratedRegex(@"(\d[\d,]*)\s*-\s*(\d[\d,]*)\s+of\s+(\d[\d,]*)\s+results?", RegexOptions.IgnoreCase)]
        private static partial Regex ResultRange();

        // The filter links address a chart and nothing else: a href that carries a query
        // string is one of the other refinements (length, language, exclusivity) pointing
        // back at the chart already open.
        [GeneratedRegex(@"<a\b[^>]*\shref=""/charts/best/([a-z0-9][a-z0-9-]*)/(\d{5,20})""[^>]*>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex CategoryLink();

        [GeneratedRegex(@"<span[^>]*class=""[^""]*\bbc-radio-label\b[^""]*""[^>]*>(.*?)</span>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex RadioLabel();

        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex Tag();

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();

        public static AudibleChartPage ParsePage(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return new AudibleChartPage([], null, null);
            }

            var asins = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in ProductListItem().Matches(html))
            {
                var asin = match.Groups[1].Value.ToUpperInvariant();
                if (seen.Add(asin))
                {
                    asins.Add(asin);
                }
            }

            var range = ResultRange().Match(html);
            int? total = null;
            int? last = null;
            if (range.Success)
            {
                if (int.TryParse(range.Groups[3].Value.Replace(",", string.Empty), out var parsedTotal))
                {
                    total = parsedTotal;
                }

                if (int.TryParse(range.Groups[2].Value.Replace(",", string.Empty), out var parsedLast))
                {
                    last = parsedLast;
                }
            }

            return new AudibleChartPage(asins, total, last);
        }

        /// <summary>
        /// The categories the page offers to switch to, including the one it is showing.
        /// "All categories" is left out: it links to <c>/charts/best</c> with no node of
        /// its own, so there is nothing to follow it by.
        /// </summary>
        public static IReadOnlyList<AudibleChartCategory> ParseCategories(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            var categories = new List<AudibleChartCategory>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in CategoryLink().Matches(html))
            {
                var slug = match.Groups[1].Value;
                var nodeId = match.Groups[2].Value;
                if (!seen.Add(nodeId))
                {
                    continue;
                }

                var inner = match.Groups[3].Value;

                // The label span holds the name as written. The aria-label beside it
                // holds the same text in single quotes, which "Children's Audiobooks"
                // breaks, so it is not used.
                var label = RadioLabel().Match(inner);
                var name = Text(label.Success ? label.Groups[1].Value : inner);
                if (name.Length == 0)
                {
                    name = SlugToName(slug);
                }

                categories.Add(new AudibleChartCategory(nodeId, slug, name));
            }

            return categories;
        }

        private static string Text(string html) =>
            Whitespace().Replace(WebUtility.HtmlDecode(Tag().Replace(html, " ")), " ").Trim();

        /// <summary>
        /// A readable name from the URL segment, for a page whose markup changed under
        /// us. "science-fiction-fantasy-audiobooks" gives "Science Fiction Fantasy".
        /// </summary>
        private static string SlugToName(string slug)
        {
            var words = slug
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => !string.Equals(word, "audiobooks", StringComparison.OrdinalIgnoreCase))
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);
            return string.Join(' ', words);
        }
    }
}
