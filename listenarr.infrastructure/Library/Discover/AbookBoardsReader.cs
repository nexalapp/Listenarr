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
using Listenarr.Application.Audiobooks.Discover;
using Listenarr.Application.Search.AbookLink;
using Listenarr.Domain.Audiobooks.Discover;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Discover
{
    /// <summary>
    /// Reads abook.link's boards: what has just been posted, board by board.
    ///
    /// <para>
    /// A listing gives a topic title and a time, and nothing else — no post is opened,
    /// no payload is revealed, nothing is thanked. The title is parsed for who wrote the
    /// book and what it is called, which is enough to show a row and to look the book up
    /// afterwards; that lookup is a separate, paced step, because a page of twenty
    /// titles would otherwise be twenty searches in a burst.
    /// </para>
    /// <para>
    /// Requests, filled requests and reading orders are posted to the same boards and
    /// are not releases, so they are left out here rather than shown as books that have
    /// become available. So are the archive imports, whose links point at a forum that
    /// no longer exists.
    /// </para>
    /// </summary>
    public sealed class AbookBoardsReader(
        IAbookLinkBrowser browser,
        IIndexerRepository indexers,
        ISecretProtector secrets,
        ILogger<AbookBoardsReader> logger,
        TimeProvider? timeProvider = null) : IDiscoverSourceReader
    {
        private const string TopicUrlFormat = "https://abook.link/book/index.php?topic={0}";

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        public string Source => DiscoverSources.AbookLink;

        public async Task<string?> UnavailableReasonAsync(CancellationToken cancellationToken = default)
        {
            // A database read, not a sign-in: the tab asks this on every load, and
            // "is one configured" is the only question it needs answered.
            foreach (var indexer in await indexers.GetAllAsync(cancellationToken))
            {
                if (AbookLinkSettings.Read(indexer.AdditionalSettings, secrets.Unprotect).HasAnything)
                {
                    return null;
                }
            }

            return "No abook.link source is configured. Add one under Settings → Indexers.";
        }

        public async Task<IReadOnlyList<DiscoverSectionOption>> ListSectionsAsync(
            CancellationToken cancellationToken = default)
        {
            var boards = await browser.ListBoardsAsync(cancellationToken);
            if (!boards.Succeeded)
            {
                logger.LogDebug("abook.link board list unavailable: {Reason}", boards.Reason);
                return [];
            }

            return boards.Items
                .Select(board => new DiscoverSectionOption(board.Id, board.Name, null, null, board.Topics))
                .ToList();
        }

        public async Task<DiscoverPage> GetPageAsync(
            DiscoverSelectedSection section,
            int page,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(section);

            var listing = await browser.BoardTopicsAsync(section.Id, page, cancellationToken);
            if (!listing.Succeeded)
            {
                return DiscoverPage.Failed(listing.Reason ?? "abook.link could not be read.");
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var items = new List<DiscoverPageItem>();
            foreach (var topic in listing.Items)
            {
                var parts = AbookTopicTitle.Parse(topic.Subject);
                if (parts.IsNotARelease || parts.IsArchiveSpot || string.IsNullOrWhiteSpace(parts.Title))
                {
                    continue;
                }

                var url = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture, TopicUrlFormat, topic.TopicId);

                items.Add(new DiscoverPageItem(
                    topic.TopicId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new DiscoverBook
                    {
                        Title = parts.Title!.Trim(),
                        Authors = parts.Author is { Length: > 0 } author ? [author.Trim()] : [],
                        Narrators = parts.Narrator is { Length: > 0 } narrator ? [narrator.Trim()] : [],
                        Series = parts.SeriesName,
                        SeriesNumber = parts.SeriesPosition,
                        PublishedDate = parts.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Link = url,
                        MetadataSource = "abook.link"
                    },
                    PostedAt: PostedAt(topic.PostedAt, now),
                    SourceUrl: url,
                    // The poster's own line, which carries what no catalog does: the
                    // bitrate, the format, which narration this is.
                    SourceNote: topic.Subject,
                    Resolved: false));
            }

            // A board has no end the listing states; an empty page is the end of it.
            return new DiscoverPage(true, items, null, listing.Items.Count > 0, null);
        }

        /// <summary>
        /// When a topic was posted, as far as can be known.
        ///
        /// <para>
        /// The forum prints its times in whatever offset the signed-in account is set
        /// to, and says nowhere what that is. On an account set ahead of UTC, "Today at
        /// 12:16 PM" reads as a moment that has not happened yet, which would sort
        /// above everything for the rest of the day and show as posted in the future.
        /// Nothing was posted after we read the page, so that is where it is pinned;
        /// the order within the day is the board's own, which the rank already carries.
        /// </para>
        /// </summary>
        private static DateTime? PostedAt(DateTime? printed, DateTime now) =>
            printed is null ? null : printed > now ? now : printed;
    }
}
