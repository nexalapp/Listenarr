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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.AbookLink
{
    /// <summary>Outcome of an abook.link request.</summary>
    /// <param name="Succeeded">Whether the body was retrieved.</param>
    /// <param name="Body">Response HTML.</param>
    /// <param name="SignedIn">
    /// False when the response came back as a logged-out page, which is the common failure
    /// and needs a different message from a network error.
    /// </param>
    /// <param name="Reason">Operator-facing explanation.</param>
    public sealed record AbookResponse(bool Succeeded, string? Body, bool SignedIn, string? Reason);

    /// <summary>
    /// Talks to abook.link.
    ///
    /// Search and topic reads cost nothing — no thanks, no tokens — so they are safe to
    /// run freely. <see cref="ThankAsync"/> is different: it posts a visible action to the
    /// operator's account, so it is never called except on a deliberate grab.
    ///
    /// Authenticates with a session cookie rather than a password, following the
    /// MyAnonamouse precedent of storing a session value instead of credentials.
    /// </summary>
    public partial class AbookLinkClient
    {
        private const string BaseUrl = "https://abook.link/book/";
        private const string FuzzySearchPath = "https://abook.link/book/tools/search_abook.php";

        [GeneratedRegex(@"action=thank;msg=(\d+);member=(\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex ThankLink();

        private readonly HttpClient _httpClient;
        private readonly ILogger<AbookLinkClient> _logger;

        public AbookLinkClient(HttpClient httpClient, ILogger<AbookLinkClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Runs the fuzzy search. Free: no thanks, no tokens.</summary>
        public Task<AbookResponse> SearchAsync(string sessionCookie, string query, CancellationToken ct = default)
        {
            var uri = new Uri(QueryHelpers.AddQueryString(FuzzySearchPath, "search", query));
            return GetAsync(sessionCookie, uri, ct);
        }

        /// <summary>Fetches a topic. Free, and returns the full NFO even while gated.</summary>
        public Task<AbookResponse> GetTopicAsync(string sessionCookie, int topicId, CancellationToken ct = default)
        {
            return GetAsync(sessionCookie, new Uri($"{BaseUrl}index.php?topic={topicId}"), ct);
        }

        /// <summary>
        /// Thanks the first post of a topic, revealing its payload.
        ///
        /// This is publicly attributed to the operator's account and cannot be assumed
        /// reversible, so it must only ever run on an explicit grab — never during search
        /// or matching. The message and member ids are read from the page rather than
        /// guessed, and a topic already thanked needs no second one.
        /// </summary>
        public async Task<AbookResponse> ThankAsync(string sessionCookie, int topicId, CancellationToken ct = default)
        {
            var topic = await GetTopicAsync(sessionCookie, topicId, ct);
            if (!topic.Succeeded || topic.Body is null)
            {
                return topic;
            }

            var link = ThankLink().Match(topic.Body);
            if (!link.Success)
            {
                // No thank link means either it is already thanked - in which case the
                // payload is present - or the post does not gate anything.
                return topic with { Reason = "No thanks was needed for this post." };
            }

            var uri = new Uri(
                $"{BaseUrl}index.php?action=thank;msg={link.Groups[1].Value};member={link.Groups[2].Value};topic={topicId};refresh=1");

            _logger.LogInformation(
                "Thanking abook.link topic {TopicId} - this is publicly visible on the configured account", topicId);

            return await GetAsync(sessionCookie, uri, ct);
        }

        private async Task<AbookResponse> GetAsync(string sessionCookie, Uri uri, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sessionCookie))
            {
                return new AbookResponse(false, null, false,
                    "No abook.link session cookie is configured.");
            }

            try
            {
                var (response, _) = await OutboundRequestSecurity.SendWithValidatedRedirectsAsync(
                    target =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, target);
                        request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
                        return request;
                    },
                    uri, _httpClient, _logger, cancellationToken: ct);

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return new AbookResponse(false, null, true,
                            $"abook.link returned HTTP {(int)response.StatusCode}.");
                    }

                    var body = await response.Content.ReadAsStringAsync(ct);
                    var signedIn = IsSignedIn(body);

                    return new AbookResponse(signedIn, body, signedIn,
                        signedIn ? null : "The abook.link session has expired. Sign in again and update the cookie.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "abook.link request failed for {Uri}", uri);
                return new AbookResponse(false, null, false, $"abook.link could not be reached: {ex.Message}");
            }
        }

        /// <summary>
        /// A logged-out SMF page still returns 200, so the status code cannot be trusted.
        /// The logout link is only rendered for a signed-in member.
        /// </summary>
        private static bool IsSignedIn(string body) =>
            body.Contains("action=logout", StringComparison.OrdinalIgnoreCase);
    }
}
