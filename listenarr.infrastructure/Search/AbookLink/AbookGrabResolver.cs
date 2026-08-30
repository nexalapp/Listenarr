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
    /// Thanks a post, reads its payload and resolves it to an NZB.
    ///
    /// The stages are separated deliberately, because each fails differently and each
    /// wants a different remedy: a post that will not reveal its payload needs a person to
    /// look at it, whereas a search string no index holds needs an NZB supplying by hand.
    /// Both are reported as themselves rather than as one generic failure.
    /// </summary>
    public class AbookGrabResolver : IAbookGrabResolver
    {
        private readonly AbookLinkClient _client;
        private readonly AbookLinkSession _session;
        private readonly IIndexerRepository _indexers;
        private readonly ISecretProtector _secrets;
        private readonly INzbResolverChain _resolvers;
        private readonly ILogger<AbookGrabResolver> _logger;

        public AbookGrabResolver(
            AbookLinkClient client,
            AbookLinkSession session,
            IIndexerRepository indexers,
            ISecretProtector secrets,
            INzbResolverChain resolvers,
            ILogger<AbookGrabResolver> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _indexers = indexers ?? throw new ArgumentNullException(nameof(indexers));
            _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _resolvers = resolvers ?? throw new ArgumentNullException(nameof(resolvers));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AbookGrabResult> ResolveAsync(int topicId, CancellationToken ct = default)
        {
            var credentials = await ResolveCredentialsAsync(ct);
            if (credentials is null || !credentials.HasAnything)
            {
                return Failed(topicId, "Not configured",
                    "No abook.link source is configured. Add a username and password in its settings.");
            }

            var signIn = await _session.GetCookieAsync(credentials, forceRefresh: false, ct);
            if (!signIn.Succeeded || signIn.Cookie is null)
            {
                return Failed(topicId, "Sign in", signIn.Reason ?? "Could not sign in to abook.link.");
            }

            // Read the post first. If it already carries a payload the account has thanked
            // it before, and thanking again would be a second public action for nothing.
            var before = await _client.GetTopicAsync(signIn.Cookie, topicId, ct);
            if (!before.Succeeded)
            {
                return Failed(topicId, "Read topic", before.Reason ?? "Could not read the topic.");
            }

            var post = Parse(before.Body, topicId);
            var thanked = false;

            if (!post.CanGrab)
            {
                _logger.LogInformation(
                    "Thanking abook.link topic {TopicId} to reveal its payload; this is publicly visible",
                    topicId);

                var revealed = await _client.ThankAsync(signIn.Cookie, topicId, ct);
                thanked = true;

                if (!revealed.Succeeded)
                {
                    return Failed(topicId, "Say thanks",
                        revealed.Reason ?? "Could not thank the post, so its payload stays hidden.",
                        post, thanked);
                }

                post = Parse(revealed.Body, topicId);
            }

            if (post.Outcome == AbookParseOutcome.NotARelease)
            {
                return Failed(topicId, "Classify",
                    "This topic is a request or a reading order, not a release.", post, thanked);
            }

            if (!post.CanGrab)
            {
                return Failed(topicId, "Read payload",
                    "The post was thanked but no search string could be read from it. "
                    + "Open the topic and copy the search string in by hand.",
                    post, thanked);
            }

            var resolution = await _resolvers.ResolveAsync(post.SearchString, ct);
            if (!resolution.Succeeded)
            {
                var detail = resolution.WorthRetrying
                    ? "No index has this release yet. It may still be propagating — try again later, or supply an NZB."
                    : "No index holds this release. Supply an NZB file to continue.";

                return new AbookGrabResult(topicId, false, "Resolve NZB", detail, thanked, post, resolution, null,
                    post.Password);
            }

            return new AbookGrabResult(topicId, true, "Done",
                $"Resolved via {resolution.ResolvedBy}.", thanked, post, resolution,
                resolution.NzbUrl, post.Password);
        }

        private static AbookPost Parse(string? html, int topicId) =>
            AbookPostParser.Parse(
                AbookPostHtml.ToText(AbookPostHtml.FirstPost(html)),
                TopicTitleOf(html));

        private static string? TopicTitleOf(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

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

        private static AbookGrabResult Failed(
            int topicId,
            string stage,
            string detail,
            AbookPost? post = null,
            bool thanked = false) =>
            new(topicId, false, stage, detail, thanked, post, null, null, post?.Password);
    }
}
