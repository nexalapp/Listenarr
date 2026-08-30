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
    /// Results link to <c>index.php?topic=&lt;id&gt;&amp;r=&lt;relevance&gt;</c>. Only the
    /// topic id is kept; relevance is the site's own ranking and Listenarr scores matches
    /// against the wanted book itself.
    /// </summary>
    public static partial class AbookSearchResultParser
    {
        [GeneratedRegex(@"<a[^>]*href=""[^""]*index\.php\?topic=(\d+)(?:[^""]*)""[^>]*>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex ResultLink();

        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex Tag();

        public static IReadOnlyList<AbookSearchHit> Parse(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            var hits = new List<AbookSearchHit>();
            var seen = new HashSet<int>();

            foreach (Match match in ResultLink().Matches(html))
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
