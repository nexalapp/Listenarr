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

namespace Listenarr.Infrastructure.Search.Providers.AbookLink
{
    /// <summary>
    /// Presents abook.link posts as search results.
    ///
    /// abook.link is a forum rather than an indexer, but what Listenarr needs from a
    /// source is candidate releases for a book, and that it does provide. Implementing the
    /// provider contract is what puts its posts in the same interactive result list as
    /// every other source, scored against the wanted book on the same terms.
    ///
    /// Nothing here costs anything. Searching and reading posts post no "thanks" and query
    /// no metered index; the payload behind a post is only revealed when a grab is asked
    /// for, which is why these results carry a topic reference rather than an NZB link.
    /// </summary>
    public class AbookLinkSearchProvider : IIndexerSearchProvider
    {
        private const int MaxTopicsInspected = 15;

        private readonly IAbookLinkBrowser _browser;
        private readonly ILogger<AbookLinkSearchProvider> _logger;

        public AbookLinkSearchProvider(IAbookLinkBrowser browser, ILogger<AbookLinkSearchProvider> logger)
        {
            _browser = browser ?? throw new ArgumentNullException(nameof(browser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string IndexerType => "AbookLink";

        public async Task<List<IndexerSearchResult>> SearchAsync(
            Indexer indexer,
            string query,
            string? category = null,
            SearchRequest? request = null)
        {
            ArgumentNullException.ThrowIfNull(indexer);

            var browse = await _browser.SearchAsync(query, MaxTopicsInspected);
            if (!browse.Succeeded)
            {
                _logger.LogWarning("abook.link search failed: {Reason}", browse.Reason);
                return [];
            }

            var results = new List<IndexerSearchResult>();

            foreach (var candidate in browse.Candidates)
            {
                var post = candidate.Post;

                // Requests and archive imports are not grabbable. Offering one wastes a
                // public "thanks" and resolves to nothing.
                if (post.Outcome is AbookParseOutcome.NotARelease or AbookParseOutcome.ArchiveSpot)
                {
                    continue;
                }

                if (!post.HasIdentity)
                {
                    continue;
                }

                results.Add(new IndexerSearchResult
                {
                    Id = $"abook:{candidate.TopicId}",
                    Title = BuildTitle(post, candidate.TopicTitle),
                    Artist = post.Author ?? "Unknown",
                    Album = post.Title ?? candidate.TopicTitle,
                    Category = "Audiobook",
                    Size = post.SizeBytes ?? 0,
                    Files = post.FileCount ?? 0,
                    Seeders = 0,
                    Leechers = 0,

                    // Deliberately no NZB link. The payload that yields one is behind a
                    // public "thanks", so it is fetched when a grab is asked for and not
                    // before - the topic reference is what a grab needs.
                    NzbUrl = string.Empty,
                    DownloadReference = candidate.TopicId.ToString(),
                    ResultUrl = $"https://abook.link/book/index.php?topic={candidate.TopicId}",
                    SourceLink = $"https://abook.link/book/index.php?topic={candidate.TopicId}",

                    DownloadType = "Usenet",
                    Format = post.Format,
                    Quality = post.Format,
                    Narrator = post.Narrator,
                    Publisher = post.Publisher,
                    Description = post.SeriesName is { Length: > 0 }
                        ? $"{post.SeriesName} {post.SeriesPosition}".Trim()
                        : null,
                    PublishedDate = post.Year?.ToString() ?? string.Empty,
                    Source = indexer.Name,
                    IndexerId = indexer.Id,
                    IndexerImplementation = indexer.Implementation
                });
            }

            _logger.LogInformation(
                "abook.link returned {Count} usable results of {Inspected} inspected for {Query}",
                results.Count, browse.Candidates.Count, query);

            return results;
        }

        /// <summary>
        /// A title that reads the way the other sources' do, built from the parsed NFO
        /// rather than the obfuscated subject the release actually carries.
        /// </summary>
        private static string BuildTitle(AbookPost post, string fallback)
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
                return fallback;
            }

            if (post.Year is { } year) title += $" ({year})";
            if (post.Format is { Length: > 0 }) title += $" [{post.Format}]";

            return title;
        }
    }
}
