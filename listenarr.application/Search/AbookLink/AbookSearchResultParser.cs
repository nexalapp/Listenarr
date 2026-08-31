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

namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>A hit from the fuzzy search, before its topic has been fetched.</summary>
    /// <param name="TopicId">SMF topic id.</param>
    /// <param name="Title">Topic title, which follows a tighter shape than post bodies.</param>
    public sealed record AbookSearchHit(int TopicId, string Title);

    /// <summary>
    /// Reads abook.link's fuzzy search results.
    ///
    /// The tool writes its anchors with single quotes, and the page carries a topic link
    /// that is not a result — a "submit feedback" link in its header. Parsing is therefore
    /// scoped to the results container rather than taking every topic link on the page,
    /// and accepts either quote style.
    /// </summary>
    public static partial class AbookSearchResultParser
    {
        [GeneratedRegex(@"<div[^>]*class\s*=\s*[""']search_results[""'][^>]*>(.*)$",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex ResultsContainer();

        [GeneratedRegex(@"<a[^>]*href\s*=\s*[""'][^""']*index\.php\?topic=(\d+)[^""']*[""'][^>]*>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex ResultLink();

        [GeneratedRegex(@"([\d,]+)\s+Results?\b", RegexOptions.IgnoreCase)]
        private static partial Regex ResultCount();

        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex Tag();

        /// <summary>
        /// Total the tool reports, which may exceed what one page lists.
        /// </summary>
        public static int? ParseTotalResults(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var match = ResultCount().Match(html);
            return match.Success && int.TryParse(match.Groups[1].Value.Replace(",", string.Empty), out var total)
                ? total
                : null;
        }

        public static IReadOnlyList<AbookSearchHit> Parse(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            // Outside the results container the page links its own feedback topic, which
            // would otherwise be returned as a result.
            var container = ResultsContainer().Match(html);
            var scope = container.Success ? container.Groups[1].Value : html;

            var hits = new List<AbookSearchHit>();
            var seen = new HashSet<int>();

            foreach (Match match in ResultLink().Matches(scope))
            {
                if (!int.TryParse(match.Groups[1].Value, out var topicId) || !seen.Add(topicId))
                {
                    continue;
                }

                var title = WebUtility.HtmlDecode(Tag().Replace(match.Groups[2].Value, string.Empty)).Trim();
                if (title.Length == 0)
                {
                    continue;
                }

                hits.Add(new AbookSearchHit(topicId, title));
            }

            return hits;
        }
    }
}
