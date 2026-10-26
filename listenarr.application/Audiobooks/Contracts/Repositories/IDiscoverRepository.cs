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

namespace Listenarr.Application.Audiobooks.Contracts.Repositories
{
    /// <summary>
    /// The discovery cache: what each followed section offered, and how far through it
    /// the fetcher has read.
    /// </summary>
    public interface IDiscoverRepository
    {
        Task<IReadOnlyList<DiscoverSection>> GetSectionsAsync(
            string source,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records what a page fetch left behind: the section's name, how deep it has
        /// been read, what the source said it holds, and why the last attempt failed.
        /// </summary>
        Task SaveSectionAsync(
            string source,
            string sectionId,
            string sectionName,
            int pagesFetched,
            int? totalItems,
            bool exhausted,
            string? lastError,
            DateTime fetchedAt,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<DiscoverItem>> GetItemsAsync(
            string source,
            IReadOnlyCollection<string> sectionIds,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds what is new and updates what moved. An item keeps the time it was first
        /// seen, because that is when the source offered it, not when we last looked.
        /// Returns how many rows were new.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="sectionId"></param>
        /// <param name="items"></param>
        /// <param name="replacePage">
        /// When set, rows previously read from that page of the section and absent from
        /// <paramref name="items"/> are deleted. That is right for a chart, where page
        /// two is positions 21 to 40 and a book that has left them has left the chart,
        /// and wrong for a forum board, where a new post pushes older topics onto the
        /// next page without anything having been withdrawn.
        /// </param>
        /// <param name="cancellationToken"></param>
        Task<int> UpsertItemsAsync(
            string source,
            string sectionId,
            IReadOnlyList<DiscoverItem> items,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Keeps the newest <paramref name="keepNewest"/> items of a section and drops
        /// the rest. A board accumulates for ever otherwise.
        /// </summary>
        Task<int> TrimSectionAsync(
            string source,
            string sectionId,
            int keepNewest,
            CancellationToken cancellationToken = default);

        /// <summary>Items still waiting on a metadata lookup, oldest first.</summary>
        Task<IReadOnlyList<DiscoverItem>> GetUnresolvedAsync(
            string source,
            int limit,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores the looked-up record against an item and marks it resolved. Marking it
        /// resolved with the record unchanged is how a lookup that found nothing is
        /// recorded, so the same fruitless search is not repeated every cycle.
        /// </summary>
        Task<bool> ResolveItemAsync(
            int id,
            DiscoverBook book,
            DateTime resolvedAt,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Drops everything cached for sections that are no longer followed. Returns how
        /// many rows went.
        /// </summary>
        Task<int> PruneSectionsAsync(
            string source,
            IReadOnlyCollection<string> keepSectionIds,
            CancellationToken cancellationToken = default);
    }
}
