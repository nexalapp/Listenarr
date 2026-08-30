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
using Listenarr.Application.Search.AbookLink;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.AbookLink
{
    /// <summary>
    /// Searches abook.link and parses what it finds.
    ///
    /// Topics are fetched one at a time rather than in parallel: this is somebody's forum,
    /// and a burst of concurrent requests for a single search is the kind of traffic that
    /// gets an account noticed.
    /// </summary>
    public class AbookLinkBrowser : IAbookLinkBrowser
    {
        private readonly AbookLinkClient _client;
        private readonly AbookLinkSession _session;
        private readonly IIndexerRepository _indexers;
        private readonly ISecretProtector _secrets;
        private readonly ILogger<AbookLinkBrowser> _logger;

        public AbookLinkBrowser(
            AbookLinkClient client,
            AbookLinkSession session,
            IIndexerRepository indexers,
            ISecretProtector secrets,
            ILogger<AbookLinkBrowser> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _indexers = indexers ?? throw new ArgumentNullException(nameof(indexers));
            _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AbookBrowseResult> SearchAsync(string query, int inspect, CancellationToken ct = default)
        {
            var credentials = await ResolveCredentialsAsync(ct);
            if (credentials is null || !credentials.HasAnything)
            {
                return Failed("No abook.link source is configured. Add a username and password in its settings.");
            }

            var response = await WithSessionAsync(credentials,
                cookie => _client.SearchAsync(cookie, query, ct), ct);

            if (!response.Succeeded)
            {
                return Failed(response.Reason ?? "abook.link search failed.");
            }

            var hits = AbookSearchResultParser.Parse(response.Body);
            var report = new AbookParseReport();
            var candidates = new List<AbookCandidate>();

            foreach (var hit in hits.Take(inspect))
            {
                ct.ThrowIfCancellationRequested();

                var topic = await WithSessionAsync(credentials,
                    cookie => _client.GetTopicAsync(cookie, hit.TopicId, ct), ct);
                if (!topic.Succeeded)
                {
                    _logger.LogDebug("Could not read topic {TopicId}: {Reason}", hit.TopicId, topic.Reason);
                    continue;
                }

                var post = AbookPostParser.Parse(topic.Body ?? string.Empty, hit.Title);
                report.Add(hit.TopicId.ToString(), post);
                candidates.Add(new AbookCandidate(hit.TopicId, hit.Title, post));
            }

            return new AbookBrowseResult(true, hits.Count, candidates, report, null);
        }

        public async Task<AbookBrowseResult> GetTopicAsync(int topicId, CancellationToken ct = default)
        {
            var credentials = await ResolveCredentialsAsync(ct);
            if (credentials is null || !credentials.HasAnything)
            {
                return Failed("No abook.link source is configured. Add a username and password in its settings.");
            }

            var topic = await WithSessionAsync(credentials,
                cookie => _client.GetTopicAsync(cookie, topicId, ct), ct);
            if (!topic.Succeeded)
            {
                return Failed(topic.Reason ?? "abook.link topic could not be read.");
            }

            var post = AbookPostParser.Parse(topic.Body ?? string.Empty, AbookTopicTitleOf(topic.Body));
            var report = new AbookParseReport();
            report.Add(topicId.ToString(), post);

            return new AbookBrowseResult(true, 1,
                [new AbookCandidate(topicId, post.Title ?? string.Empty, post)], report, null);
        }

        private static string? AbookTopicTitleOf(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            // SMF titles the page "Board - Topic title"; the topic is what follows the
            // first separator.
            const string open = "<title>";
            const string close = "</title>";
            var start = body.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            start += open.Length;
            var end = body.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return null;
            }

            var title = System.Net.WebUtility.HtmlDecode(body[start..end]).Trim();
            var separator = title.IndexOf(" - ", StringComparison.Ordinal);
            return separator >= 0 ? title[(separator + 3)..] : title;
        }

        /// <summary>
        /// Runs a request with a session, signing in again once if the forum shows a
        /// logged-out page. An expired session then recovers by itself rather than
        /// surfacing as a failure the operator has to act on.
        /// </summary>
        private async Task<AbookResponse> WithSessionAsync(
            AbookCredentials credentials,
            Func<string, Task<AbookResponse>> request,
            CancellationToken ct)
        {
            var signIn = await _session.GetCookieAsync(credentials, forceRefresh: false, ct);
            if (!signIn.Succeeded || signIn.Cookie is null)
            {
                return new AbookResponse(false, null, false, signIn.Reason);
            }

            var response = await request(signIn.Cookie);
            if (response.SignedIn || !credentials.CanSignIn)
            {
                return response;
            }

            _logger.LogInformation("abook.link session expired; signing in again");
            AbookLinkSession.Invalidate(credentials);

            var renewed = await _session.GetCookieAsync(credentials, forceRefresh: true, ct);
            return renewed.Succeeded && renewed.Cookie is not null
                ? await request(renewed.Cookie)
                : new AbookResponse(false, null, false, renewed.Reason);
        }

        private async Task<AbookCredentials?> ResolveCredentialsAsync(CancellationToken ct)
        {
            foreach (var indexer in await _indexers.GetAllAsync(ct))
            {
                var credentials = AbookLinkSettings.Read(indexer.AdditionalSettings, _secrets.Unprotect);
                if (credentials.HasAnything)
                {
                    return credentials;
                }
            }

            return null;
        }

        private static AbookBrowseResult Failed(string reason) =>
            new(false, 0, [], new AbookParseReport(), reason);
    }
}
