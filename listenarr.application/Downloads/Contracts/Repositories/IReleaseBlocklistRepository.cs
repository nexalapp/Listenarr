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

namespace Listenarr.Application.Downloads.Contracts.Repositories
{
    /// <summary>The releases that failed for their own reasons and must not be grabbed again.</summary>
    public interface IReleaseBlocklistRepository
    {
        /// <summary>
        /// Every release blocked for one book. Read on every automatic search, so it is
        /// scoped to the book rather than returning a list that grows with the library.
        /// </summary>
        Task<List<BlockedRelease>> GetForAudiobookAsync(int audiobookId, CancellationToken ct = default);

        /// <summary>
        /// Records a block, or leaves the existing one alone when the same release is
        /// already blocked for the same book - the same release can fail twice while two
        /// grabs are in flight, and that is one block, not two.
        /// </summary>
        Task<BlockedRelease> AddAsync(BlockedRelease entry, CancellationToken ct = default);

        /// <summary>The most recent blocks, newest first, for a person reading the list.</summary>
        Task<List<BlockedRelease>> GetRecentAsync(int limit, CancellationToken ct = default);

        /// <summary>Unblocks one release, so it can be grabbed again.</summary>
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>Unblocks every release blocked for one book.</summary>
        Task<int> DeleteForAudiobookAsync(int audiobookId, CancellationToken ct = default);
    }
}
