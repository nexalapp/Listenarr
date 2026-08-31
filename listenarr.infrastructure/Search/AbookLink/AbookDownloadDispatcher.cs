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
using Listenarr.Application.Search.AbookLink;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.AbookLink
{
    /// <summary>Outcome of handing a resolved release to a download client.</summary>
    /// <param name="Succeeded">Whether the client accepted it.</param>
    /// <param name="ClientName">Which client it went to.</param>
    /// <param name="DownloadId">The client's own identifier for the queued item.</param>
    /// <param name="PasswordSent">
    /// Whether an archive password accompanied it. Stated plainly because a missing
    /// password does not fail here — it fails much later, at extraction, looking like a
    /// corrupt download.
    /// </param>
    /// <param name="Detail">What happened, for an operator.</param>
    public sealed record AbookDispatchResult(
        bool Succeeded,
        string? ClientName,
        string? DownloadId,
        bool PasswordSent,
        string Detail);

    /// <summary>
    /// Sends a resolved abook.link release to a download client.
    ///
    /// Kept apart from resolving so the two fail separately: an NZB nothing can resolve
    /// and an NZB no client will accept want different answers, and collapsing them loses
    /// which happened.
    /// </summary>
    public partial class AbookDownloadDispatcher
    {
        [GeneratedRegex(@"[^\w\-. ]")]
        private static partial Regex UnsafeFileNameChars();

        private readonly INzbFileDownloader _downloader;
        private readonly IDownloadClientConfigurationRepository _clients;
        private readonly IDownloadClientAdapterFactory _adapters;
        private readonly ILogger<AbookDownloadDispatcher> _logger;

        public AbookDownloadDispatcher(
            INzbFileDownloader downloader,
            IDownloadClientConfigurationRepository clients,
            IDownloadClientAdapterFactory adapters,
            ILogger<AbookDownloadDispatcher> logger)
        {
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _clients = clients ?? throw new ArgumentNullException(nameof(clients));
            _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AbookDispatchResult> SendAsync(
            AbookPost post,
            string nzbUrl,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(post);

            var client = (await _clients.GetAllAsync(ct))
                .FirstOrDefault(candidate => candidate.IsEnabled
                    && candidate.Type is "nzbget" or "sabnzbd");

            if (client is null)
            {
                return new AbookDispatchResult(false, null, null, false,
                    "No usenet download client is enabled. Add SABnzbd or NZBGet under Settings, Clients.");
            }

            byte[] nzb;
            try
            {
                nzb = await _downloader.DownloadAsync(nzbUrl, null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new AbookDispatchResult(false, client.Name, null, false,
                    $"The NZB could not be fetched: {ex.Message}");
            }

            if (nzb.Length == 0)
            {
                return new AbookDispatchResult(false, client.Name, null, false,
                    "The index returned an empty NZB.");
            }

            var title = BuildTitle(post);
            var submission = new PreparedUsenetSubmission(
                title,
                post.Author ?? string.Empty,
                post.Title ?? string.Empty,
                "abook.link",
                post.Format,
                null,
                post.SizeBytes ?? 0,
                nzbUrl,
                nzb,
                $"{SanitizeFileName(title)}.nzb",
                post.Password);

            try
            {
                var adapter = _adapters.GetByType(client.Type);
                var result = await adapter.AddAsync(client, submission, ct);

                _logger.LogInformation(
                    "Sent '{Title}' to {Client}{WithPassword}",
                    title, client.Name, post.Password is { Length: > 0 } ? " with an archive password" : string.Empty);

                return new AbookDispatchResult(
                    true, client.Name, result.ExternalId, post.Password is { Length: > 0 },
                    $"Queued on {client.Name}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "{Client} rejected '{Title}'", client.Name, title);
                return new AbookDispatchResult(false, client.Name, null, false,
                    $"{client.Name} rejected it: {ex.Message}");
            }
        }

        /// <summary>
        /// A name a person will recognise in the download queue, built from what the post
        /// actually told us rather than the obfuscated Usenet subject.
        /// </summary>
        private static string BuildTitle(AbookPost post)
        {
            var parts = new List<string>();

            if (post.Author is { Length: > 0 }) parts.Add(post.Author);
            if (post.SeriesName is { Length: > 0 })
            {
                parts.Add(post.SeriesPosition is { Length: > 0 }
                    ? $"{post.SeriesName} {post.SeriesPosition}"
                    : post.SeriesName);
            }

            if (post.Title is { Length: > 0 }) parts.Add(post.Title);

            var title = string.Join(" - ", parts);
            if (title.Length == 0)
            {
                title = post.SearchString ?? "abook.link release";
            }

            return post.Year is { } year ? $"{title} ({year})" : title;
        }

        private static string SanitizeFileName(string value) =>
            UnsafeFileNameChars().Replace(value, "_").Trim();
    }
}
