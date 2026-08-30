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

        public NzbKingResolver(NzbKingApiClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public string Name => "NZBKing";

        public int Order => 30;

        /// <summary>Set from the indexer's settings before the chain runs.</summary>
        public string? ApiKey { get; set; }

        public async Task<NzbResolverResult> ResolveAsync(string searchString, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                return NzbResolverResult.Failed(Name, NzbResolutionFailure.Unavailable,
                    "No NZBKing API key is configured, so this index was not asked.");
            }

            var result = await _client.SearchAsync(ApiKey, searchString, NzbKingAccessPurpose.Grab, ct);

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
    }
}
