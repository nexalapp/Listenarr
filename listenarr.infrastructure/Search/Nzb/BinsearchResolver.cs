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
using System.Text;
using Listenarr.Application.Search.Nzb;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.Nzb
{
    /// <summary>
    /// Resolves through Binsearch.
    ///
    /// Serves the same index as NZBIndex, so it adds no coverage — it is here because its
    /// <c>/nzb</c> endpoint assembles several parts into one NZB. Releases whose poster
    /// says they "don't show up as one collection" cannot be fetched any other verified
    /// way.
    /// </summary>
    public class BinsearchResolver : INzbResolver
    {
        private const string SearchEndpoint = "https://binsearch.info/search";
        private const string NzbEndpoint = "https://binsearch.info/nzb";

        private readonly HttpClient _httpClient;
        private readonly ILogger<BinsearchResolver> _logger;

        public BinsearchResolver(HttpClient httpClient, ILogger<BinsearchResolver> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => "Binsearch";

        public int Order => 20;

        public async Task<NzbResolverResult> ResolveAsync(string searchString, CancellationToken ct = default)
        {
            var uri = new Uri(QueryHelpers.AddQueryString(SearchEndpoint, new Dictionary<string, string?>
            {
                ["q"] = searchString,
                ["max"] = "25"
            }));

            try
            {
                var (response, _) = await OutboundRequestSecurity.SendWithValidatedRedirectsAsync(
                    target => new HttpRequestMessage(HttpMethod.Get, target),
                    uri, _httpClient, _logger, cancellationToken: ct);

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return NzbResolverResult.Failed(Name, NzbResolutionFailure.Unavailable,
                            $"Binsearch returned HTTP {(int)response.StatusCode}.");
                    }

                    var candidates = BinsearchResultParser.Parse(await response.Content.ReadAsStringAsync(ct));
                    return NzbResolverChain.SelectBest(Name, candidates,
                        _ => BuildNzbUrl(candidates, searchString));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Binsearch lookup failed for {Search}", searchString);
                return NzbResolverResult.Failed(Name, NzbResolutionFailure.Unavailable,
                    $"Binsearch could not be reached: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the NZB URL, selecting every complete part rather than only the first.
        /// A release split across parts needs them all in one NZB; taking a single hit
        /// would download a fragment.
        /// </summary>
        internal static string BuildNzbUrl(IReadOnlyList<NzbCandidate> candidates, string searchString)
        {
            var wanted = candidates.Where(c => c.Complete != false).ToList();
            if (wanted.Count == 0)
            {
                wanted = candidates.ToList();
            }

            var url = new StringBuilder(NzbEndpoint).Append('?');
            foreach (var candidate in wanted)
            {
                url.Append(Uri.EscapeDataString(candidate.Id)).Append("=on&");
            }

            return url.Append("q=").Append(Uri.EscapeDataString(searchString)).ToString();
        }
    }
}
