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
        private readonly IIndexerRepository _indexers;
        private readonly ILogger<AbookLinkBrowser> _logger;

        public AbookLinkBrowser(
            AbookLinkClient client,
            IIndexerRepository indexers,
            ILogger<AbookLinkBrowser> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _indexers = indexers ?? throw new ArgumentNullException(nameof(indexers));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AbookBrowseResult> SearchAsync(string query, int inspect, CancellationToken ct = default)
        {
            var cookie = await ResolveCookieAsync(ct);
            if (cookie is null)
            {
                return Failed("No abook.link source is configured with a session cookie.");
            }

            var response = await _client.SearchAsync(cookie, query, ct);
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

                var topic = await _client.GetTopicAsync(cookie, hit.TopicId, ct);
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
            var cookie = await ResolveCookieAsync(ct);
            if (cookie is null)
            {
                return Failed("No abook.link source is configured with a session cookie.");
            }

            var topic = await _client.GetTopicAsync(cookie, topicId, ct);
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

        private async Task<string?> ResolveCookieAsync(CancellationToken ct)
        {
            foreach (var indexer in await _indexers.GetAllAsync(ct))
            {
                var cookie = AbookLinkSettings.TryGetSessionCookie(indexer.AdditionalSettings);
                if (cookie is { Length: > 0 })
                {
                    return cookie;
                }
            }

            return null;
        }

        private static AbookBrowseResult Failed(string reason) =>
            new(false, 0, [], new AbookParseReport(), reason);
    }
}
