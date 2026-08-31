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
using Listenarr.Application.Search.Nzb;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.Nzb
{
    /// <summary>
    /// Resolves through NZBIndex's JSON API. First in the chain: free, unmetered and
    /// structured, so nothing is scraped and no allowance is spent.
    /// </summary>
    public class NzbIndexResolver : INzbResolver
    {
        private const string SearchEndpoint = "https://nzbindex.nl/api/search";
        private const string DownloadFormat = "https://nzbindex.nl/download/{0}.nzb";
        private const int PageSize = 25;

        private readonly HttpClient _httpClient;
        private readonly ILogger<NzbIndexResolver> _logger;

        public NzbIndexResolver(HttpClient httpClient, ILogger<NzbIndexResolver> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => "NZBIndex";

        public int Order => 10;

        public async Task<NzbResolverResult> ResolveAsync(string searchString, CancellationToken ct = default)
        {
            // Without an explicit size the API answers with an empty page rather than a
            // default one, which reads as "nothing indexed" and is not.
            var uri = new Uri(QueryHelpers.AddQueryString(SearchEndpoint, new Dictionary<string, string?>
            {
                ["q"] = searchString,
                ["size"] = PageSize.ToString()
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
                            $"NZBIndex returned HTTP {(int)response.StatusCode}.");
                    }

                    var candidates = NzbIndexResponseParser.Parse(await response.Content.ReadAsStringAsync(ct));
                    return NzbResolverChain.SelectBest(Name, candidates,
                        id => string.Format(DownloadFormat, id));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "NZBIndex lookup failed for {Search}", searchString);
                return NzbResolverResult.Failed(Name, NzbResolutionFailure.Unavailable,
                    $"NZBIndex could not be reached: {ex.Message}");
            }
        }
    }
}
