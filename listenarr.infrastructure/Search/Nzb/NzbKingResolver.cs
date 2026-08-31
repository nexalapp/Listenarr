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
using Listenarr.Application.Search.Nzb;
using Listenarr.Application.Search.NzbKing;
using Listenarr.Infrastructure.Search.NzbKing;

namespace Listenarr.Infrastructure.Search.Nzb
{
    /// <summary>
    /// Resolves through NZBKing. Last in the chain, and the only genuinely independent
    /// index: NZBIndex and Binsearch serve the same articles, so NZBKing is the only one
    /// that can hold a release the others lack.
    ///
    /// Every request costs a token from a small self-deleting allowance, so it runs behind
    /// the budget and is asked only when the free indexes have nothing.
    /// </summary>
    public partial class NzbKingResolver : INzbResolver
    {
        // The feed's exact structure is not pinned - confirming it costs a token - but the
        // NZB URL format is verified, so links are lifted from the body directly rather
        // than assuming an element layout that may not hold.
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
        /// Overrides the configured key. Left unset in normal use — the resolver reads the
        /// key itself so there is no ordering rule for a caller to get wrong.
        /// </summary>
        public string? ApiKey { get; set; }

        public async Task<NzbResolverResult> ResolveAsync(string searchString, CancellationToken ct = default)
        {
            var apiKey = ApiKey ?? await FindApiKeyAsync(ct);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return NzbResolverResult.Failed(Name, NzbResolutionFailure.Unavailable,
                    "No NZBKing API key is configured, so this index was not asked.");
            }

            var result = await _client.SearchAsync(apiKey, searchString, NzbKingAccessPurpose.Grab, ct);

            if (!result.Succeeded)
            {
                var failure = result.Reason?.Contains("budget", StringComparison.OrdinalIgnoreCase) == true
                    ? NzbResolutionFailure.BudgetExhausted
                    : NzbResolutionFailure.Unavailable;

                return NzbResolverResult.Failed(Name, failure,
                    result.Reason ?? "NZBKing did not answer.");
            }

            var match = NzbLink().Match(result.Body ?? string.Empty);
            if (!match.Success)
            {
                return NzbResolverResult.Failed(Name, NzbResolutionFailure.NotIndexed,
                    "NZBKing answered but holds nothing for this search string.");
            }

            var id = match.Groups[1].Value;
            return NzbResolverResult.Found(Name, $"https://nzbking.com/{id}/",
                [new NzbCandidate(id, searchString)]);
        }

        /// <summary>
        /// Finds the key on whichever source carries one.
        ///
        /// Read here rather than pushed in by a caller: a property somebody has to
        /// remember to set is a property that goes unset, and the symptom - "no API key is
        /// configured" for a key that plainly is - gives no hint where to look.
        /// </summary>
        private async Task<string?> FindApiKeyAsync(CancellationToken ct)
        {
            foreach (var indexer in await _indexers.GetAllAsync(ct))
            {
                var key = NzbKingSettings.TryGetApiKey(indexer.AdditionalSettings);
                if (key is { Length: > 0 })
                {
                    return key;
                }
            }

            return null;
        }
    }
}
