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

namespace Listenarr.Application.Audiobooks.Contracts.Repositories
{
    /// <summary>
    /// Durable storage for tag-writing jobs.
    ///
    /// Claiming is a repository concern rather than a service one because it has to be
    /// atomic: two workers reading then writing would both believe they own the job, and
    /// two rewrites of one file racing to publish is exactly the failure this whole path
    /// is built to avoid.
    /// </summary>
    public interface ITagJobRepository
    {
        Task<TagJob?> GetAsync(Guid id, CancellationToken cancellationToken = default);

        Task<TagJob?> GetActiveForAudiobookAsync(
            int audiobookId,
            CancellationToken cancellationToken = default);

        /// <summary>Active jobs plus terminal ones newer than <paramref name="terminalSince"/>.</summary>
        Task<IReadOnlyList<TagJob>> GetVisibleAsync(
            DateTime terminalSince,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Insert a job. Returns null when the unique active-deduplication index rejects
        /// it, which means another caller queued the same book first.
        /// </summary>
        Task<TagJob?> AddAsync(TagJob job, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically take the next runnable job for <paramref name="leaseOwner"/>, moving
        /// it to Running with a fresh lease. Returns null when nothing is runnable.
        /// </summary>
        Task<TagJob?> ClaimNextAsync(
            string leaseOwner,
            DateTime now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Extend a lease this worker still owns. False means the lease was lost and the
        /// worker must abandon the job rather than keep writing to it.
        /// </summary>
        Task<bool> RenewLeaseAsync(
            Guid id,
            string leaseOwner,
            DateTime expiresAt,
            CancellationToken cancellationToken = default);

        /// <summary>Apply a change to a job and save. Returns false when the job has gone.</summary>
        Task<bool> UpdateAsync(
            Guid id,
            Action<TagJob> mutate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Return jobs whose lease has expired to the queue. A restart leaves rows Running
        /// with a lease nobody will renew, and those would otherwise never be picked up.
        /// </summary>
        Task<int> ReleaseExpiredLeasesAsync(
            DateTime now,
            CancellationToken cancellationToken = default);
    }
}
