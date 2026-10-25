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
using Listenarr.Domain.Audiobooks.Discover;

namespace Listenarr.Application.Audiobooks.Discover
{
    /// <summary>
    /// The discovery tabs: what other people are buying, and what has just been posted.
    /// Reading is answered from cached rows and never touches the network.
    /// </summary>
    public interface IDiscoverService
    {
        /// <summary>Everything a tab shows, from the cache.</summary>
        Task<DiscoverFeed> GetFeedAsync(string source, CancellationToken cancellationToken = default);

        /// <summary>
        /// What the source offers to follow, and what is followed now. This one does go
        /// to the source — it is the picker opening — but only for the index page, and
        /// the answer is held briefly so opening the picker twice costs one request.
        /// </summary>
        Task<DiscoverSectionCatalog> GetSectionsAsync(string source, CancellationToken cancellationToken = default);

        /// <summary>Changes which sections a tab draws from. Returns what was stored.</summary>
        Task<DiscoverSelection> SaveSelectionAsync(
            string source,
            SaveDiscoverSelectionRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Fills the discovery cache. The only part of the feature that reaches a source.
    ///
    /// <para>
    /// A chart category is a hundred books and a board is thousands, so the work is
    /// paced: one page, or one metadata lookup, per background tick. "Refresh" re-reads
    /// the first page of each followed section — which is where anything new is — and
    /// "load more" takes the next page nobody has read yet.
    /// </para>
    /// </summary>
    public interface IDiscoverRefreshService
    {
        DiscoverRefreshStatus StatusFor(string source);

        /// <summary>Re-read the head of every followed section. False when one is already running.</summary>
        Task<bool> RequestRefreshAsync(string source, CancellationToken cancellationToken = default);

        /// <summary>
        /// Read one more page of every followed section, beyond what is cached. False
        /// when a fetch is already running, or when every section is as deep as it goes.
        /// </summary>
        Task<bool> RequestMoreAsync(string source, CancellationToken cancellationToken = default);

        /// <summary>
        /// One unit of work a refresh would do — a page nobody has read, or a forum post
        /// nobody has looked up — and a word for what it was. Null when there is nothing
        /// to do, or when a refresh is already fetching, so the background worker never
        /// adds to a burst.
        /// </summary>
        Task<string?> FetchNextNeededAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One discovery source, as the refresher sees it: a list of sections, and a page of
    /// one of them. Implemented in infrastructure, where the HTTP lives.
    /// </summary>
    public interface IDiscoverSourceReader
    {
        /// <summary>One of <see cref="DiscoverSources"/>.</summary>
        string Source { get; }

        /// <summary>
        /// Why this source cannot be used, or null when it can. Says, for instance, that
        /// no abook.link indexer is configured — which is not an error, just a reason the
        /// tab is empty.
        /// </summary>
        Task<string?> UnavailableReasonAsync(CancellationToken cancellationToken = default);

        /// <summary>The sections the source lists: chart categories, or forum boards.</summary>
        Task<IReadOnlyList<DiscoverSectionOption>> ListSectionsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// One page of a section, newest or highest first. <paramref name="page"/> is
        /// 1-based.
        /// </summary>
        Task<DiscoverPage> GetPageAsync(
            DiscoverSelectedSection section,
            int page,
            CancellationToken cancellationToken = default);
    }

    /// <summary>One page of a section.</summary>
    /// <param name="Succeeded"></param>
    /// <param name="Items"></param>
    /// <param name="Total">
    /// How many items the source says the section holds, when it says. Audible's chart
    /// does; a forum board does not.
    /// </param>
    /// <param name="HasMore">Whether a further page exists.</param>
    /// <param name="Reason"></param>
    public sealed record DiscoverPage(
        bool Succeeded,
        IReadOnlyList<DiscoverPageItem> Items,
        int? Total,
        bool HasMore,
        string? Reason)
    {
        public static DiscoverPage Failed(string reason) => new(false, [], null, false, reason);
    }

    /// <summary>
    /// One item on a page. <paramref name="Resolved"/> is false when the source gave
    /// only a line of text — a forum topic title — and the book still needs looking up.
    /// </summary>
    public sealed record DiscoverPageItem(
        string ExternalId,
        DiscoverBook Book,
        DateTime? PostedAt = null,
        string? SourceUrl = null,
        string? SourceNote = null,
        bool Resolved = true);

    /// <summary>
    /// Turns what a forum title said into a catalog record, by looking the book up.
    /// Implemented over the same Audible lookups the add flow uses.
    /// </summary>
    public interface IDiscoverMetadataResolver
    {
        /// <summary>
        /// The book the source named, filled in. Null when nothing convincing was found;
        /// the caller keeps what it had and does not ask again.
        /// </summary>
        Task<DiscoverBook?> ResolveAsync(DiscoverBook unresolved, CancellationToken cancellationToken = default);
    }
}
