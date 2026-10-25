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
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Listenarr.Application.Search.Nzb;
using Listenarr.Infrastructure.Search.NzbKing;

namespace Listenarr.Infrastructure.Search.Nzb
{
    /// <summary>
    /// Resolves through NZBKing. Last in the chain, and the only genuinely independent
    /// index: NZBIndex and Binsearch serve the same articles, so NZBKing is the only one
    /// that can hold a release the others lack.
    ///
    /// Every request costs a token from a small self-deleting allowance, so it runs behind
    /// the budget and is asked only when the free indexes have nothing. Several keys may be
    /// configured, each with an allowance of its own; the budget decides which one pays.
    ///
    /// The keys are read here rather than pushed in by a caller: a property somebody has to
    /// remember to set is a property that goes unset, and the symptom - "no API key is
    /// configured" for a key that plainly is - gives no hint where to look.
    /// </summary>
    public partial class NzbKingResolver : INzbResolver
    {
        [GeneratedRegex(@"nzbking\.com/(nzb:[0-9a-f]{24})/?", RegexOptions.IgnoreCase)]
        private static partial Regex NzbLink();

        private readonly NzbKingApiClient _client;
        private readonly IIndexerRepository _indexers;

        public NzbKingResolver(NzbKingApiClient client, IIndexerRepository indexers)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _indexers = indexers ?? throw new ArgumentNullException(nameof(indexers));
        }

        public string Name => "NZBKing";

        public int Order => 30;

        /// <summary>
        /// Overrides the configured keys. Left unset in normal use — the resolver reads
        /// them itself so there is no ordering rule for a caller to get wrong.
        /// </summary>
        public string? ApiKey { get; set; }

        public async Task<NzbResolverResult> ResolveAsync(string searchString, CancellationToken ct = default)
        {
            IReadOnlyList<string> apiKeys = ApiKey is { Length: > 0 } overridden
                ? [overridden]
                : await NzbKingConfiguredKeys.CollectAsync(_indexers, ct);

            if (apiKeys.Count == 0)
            {
                return NzbResolverResult.Failed(Name, NzbResolutionFailure.Unavailable,
                    "No NZBKing API key is configured, so this index was not asked.");
            }

            var result = await _client.SearchFromPoolAsync(
                apiKeys, NzbSearchStringMatch.QueryFor(searchString), NzbKingAccessPurpose.Grab, ct);

            if (!result.Succeeded)
            {
                var failure = result.Reason?.Contains("budget", StringComparison.OrdinalIgnoreCase) == true
                    ? NzbResolutionFailure.BudgetExhausted
                    : NzbResolutionFailure.Unavailable;

                return NzbResolverResult.Failed(Name, failure,
                    result.Reason ?? "NZBKing did not answer.");
            }

            var candidates = ParseFeed(result.Body);
            if (candidates is null)
            {
                return NzbResolverResult.Failed(Name, NzbResolutionFailure.Unavailable,
                    "NZBKing answered with something other than its RSS feed.");
            }

            if (candidates.Count == 0)
            {
                return NzbResolverResult.Failed(Name, NzbResolutionFailure.NotIndexed,
                    "NZBKing answered but holds nothing for this search string.");
            }

            // The feed is a word search, newest first: even asked for the code alone it may
            // list other uploads, and the first of them can be someone else's book. Only an
            // item carrying the whole search string is this release.
            var matching = NzbSearchStringMatch.Filter(searchString, candidates);
            if (matching.Count == 0)
            {
                return NzbResolverChain.NotThisRelease(Name, candidates);
            }

            return NzbResolverResult.Found(Name, $"https://nzbking.com/{matching[0].Id}/", matching);
        }

        /// <summary>
        /// Reads the search feed's items: the subject from <c>title</c> and the NZB id from
        /// <c>link</c> (or <c>guid</c>), which point at <c>nzbking.com/nzb:&lt;id&gt;/</c>.
        /// Returns null when the body is not an RSS feed at all - NZBKing serves an HTML
        /// page with a 200 for a request it will not answer - so that is not mistaken for
        /// an index holding nothing.
        /// </summary>
        internal static IReadOnlyList<NzbCandidate>? ParseFeed(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            XDocument document;
            try
            {
                // The real feed opens with a blank line before its XML declaration, which
                // the parser otherwise refuses outright.
                document = XDocument.Parse(body.TrimStart());
            }
            catch (XmlException)
            {
                return null;
            }

            if (document.Root?.Name.LocalName != "rss")
            {
                return null;
            }

            var candidates = new List<NzbCandidate>();
            foreach (var item in document.Descendants("item"))
            {
                var link = NzbLink().Match((string?)item.Element("link") ?? string.Empty);
                if (!link.Success)
                {
                    link = NzbLink().Match((string?)item.Element("guid") ?? string.Empty);
                }

                if (!link.Success)
                {
                    continue;
                }

                candidates.Add(new NzbCandidate(
                    link.Groups[1].Value,
                    ((string?)item.Element("title") ?? string.Empty).Trim()));
            }

            return candidates;
        }
    }
}
