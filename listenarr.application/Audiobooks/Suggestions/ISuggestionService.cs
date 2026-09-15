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
namespace Listenarr.Application.Audiobooks.Suggestions
{
    public interface ISuggestionService
    {
        /// <summary>Compute suggestions from cached catalogs; never touches the network.</summary>
        Task<SuggestionSnapshot> GetAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Fills the catalog caches the suggestions are computed from. This is the only part
    /// of the feature that goes to the network, and it runs only when asked: from the
    /// page's refresh button, or when a book is added and its author or series has no
    /// catalog yet.
    /// </summary>
    public interface ISuggestionRefreshService
    {
        SuggestionRefreshStatus Status { get; }

        /// <summary>
        /// Fetch a catalog for every library author and series that has none, or whose
        /// cached one is older than <paramref name="staleAfter"/>. Returns false when a
        /// refresh is already running.
        /// </summary>
        Task<bool> RequestRefreshAsync(TimeSpan? staleAfter = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetch catalogs for specific names when they are not cached yet. Cheap to call
        /// on every add; it does nothing for names already covered.
        /// </summary>
        void QueueIfMissing(IEnumerable<string> authors, IEnumerable<string> series);
    }
}
