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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.NzbKing
{
    /// <summary>
    /// Outcome of a metered NZBKing request.
    /// </summary>
    /// <param name="Succeeded">Whether a response body was obtained.</param>
    /// <param name="Body">Response content, when the request was made and succeeded.</param>
    /// <param name="Reason">Why nothing was fetched, for surfacing to an operator.</param>
    public sealed record NzbKingApiResult(bool Succeeded, string? Body, string? Reason);

    /// <summary>
    /// The only route to NZBKing's API.
    ///
    /// Every request costs a token from a small, self-deleting allowance, so nothing here
    /// runs without first acquiring a lease from <see cref="INzbKingTokenBudget"/>. Callers
    /// must not construct their own requests to NZBKing; the budget can only protect the
    /// key if it sees every call.
    /// </summary>
    public class NzbKingApiClient
    {
        private const string SearchFeedPath = "https://nzbking.com/rss/search/";

        private readonly HttpClient _httpClient;
        private readonly INzbKingTokenBudget _budget;
        private readonly ILogger<NzbKingApiClient> _logger;

        public NzbKingApiClient(
            HttpClient httpClient,
            INzbKingTokenBudget budget,
            ILogger<NzbKingApiClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _budget = budget ?? throw new ArgumentNullException(nameof(budget));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Runs one budgeted search against NZBKing's RSS feed.
        /// Returns without contacting NZBKing when the budget refuses.
        /// </summary>
        public async Task<NzbKingApiResult> SearchAsync(
            string apiKey,
            string query,
            NzbKingAccessPurpose purpose,
            CancellationToken ct = default)
        {
            var lease = await _budget.TryAcquireAsync(apiKey, purpose, query, ct);
            if (!lease.Granted)
            {
                return new NzbKingApiResult(false, null, lease.Reason);
            }

            var uri = new Uri(QueryHelpers.AddQueryString(
                SearchFeedPath,
                new Dictionary<string, string?> { ["q"] = query, ["key"] = apiKey }));

            var status = 0;
            try
            {
                var (response, _) = await OutboundRequestSecurity.SendWithValidatedRedirectsAsync(
                    target => new HttpRequestMessage(HttpMethod.Get, target),
                    uri,
                    _httpClient,
                    _logger,
                    cancellationToken: ct);

                using (response)
                {
                    status = (int)response.StatusCode;
                    if (!response.IsSuccessStatusCode)
                    {
                        return new NzbKingApiResult(false, null,
                            status == 429
                                ? "NZBKing rejected the key (429). It has been deleted and must be replaced."
                                : $"NZBKing returned HTTP {status}.");
                    }

                    var body = await response.Content.ReadAsStringAsync(ct);
                    return new NzbKingApiResult(true, body, null);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "NZBKing request failed for query {Query}", query);
                return new NzbKingApiResult(false, null, $"NZBKing request failed: {ex.Message}");
            }
            finally
            {
                // Reconcile the spend even on failure: the token is gone either way, and a
                // 429 here is how we learn the key has been deleted.
                await _budget.ReportOutcomeAsync(lease, status, ct);
            }
        }
    }
}
