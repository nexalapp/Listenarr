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
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    public sealed class EfTagJobRepository(ListenArrDbContext db) : ITagJobRepository
    {
        private static readonly TagJobStatus[] ActiveStatuses =
        [
            TagJobStatus.Queued,
            TagJobStatus.Running,
            TagJobStatus.RetryScheduled
        ];

        public Task<TagJob?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            db.TagJobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken);

        public Task<TagJob?> GetActiveForAudiobookAsync(
            int audiobookId,
            CancellationToken cancellationToken = default) =>
            db.TagJobs
                .AsNoTracking()
                .Where(job => job.AudiobookId == audiobookId && ActiveStatuses.Contains(job.Status))
                .OrderByDescending(job => job.EnqueuedAt)
                .FirstOrDefaultAsync(cancellationToken);

        public async Task<IReadOnlyList<TagJob>> GetVisibleAsync(
            DateTime terminalSince,
            CancellationToken cancellationToken = default) =>
            await db.TagJobs
                .AsNoTracking()
                .Where(job =>
                    ActiveStatuses.Contains(job.Status)
                    || (job.CompletedAt != null && job.CompletedAt >= terminalSince))
                .OrderByDescending(job => job.EnqueuedAt)
                .ToListAsync(cancellationToken);

        public async Task<TagJob?> AddAsync(
            TagJob job,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(job);

            db.TagJobs.Add(job);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return job;
            }
            catch (DbUpdateException)
            {
                // The unique index over ActiveDeduplicationKey rejected it, which means a
                // concurrent caller queued the same book first. That is the intended
                // outcome, not an error: one tag write per book, because two workers
                // rewriting the same file would race to replace it.
                db.Entry(job).State = EntityState.Detached;
                return null;
            }
        }

        public async Task<TagJob?> ClaimNextAsync(
            string leaseOwner,
            DateTime now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);

            // Loop rather than claim blindly: between selecting a candidate and writing the
            // lease another worker may have taken it, and the concurrency check below is
            // what detects that. Retrying moves on to the next candidate.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var candidate = await db.TagJobs
                    .Where(job =>
                        (job.Status == TagJobStatus.Queued
                         || job.Status == TagJobStatus.RetryScheduled)
                        && (job.NextAttemptAt == null || job.NextAttemptAt <= now)
                        && (job.LeaseExpiresAt == null || job.LeaseExpiresAt <= now))
                    .OrderBy(job => job.EnqueuedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (candidate == null)
                {
                    return null;
                }

                var observedGeneration = candidate.LeaseGeneration;

                candidate.Status = TagJobStatus.Running;
                candidate.Phase = TagJobPhase.Reading;
                candidate.LeaseOwner = leaseOwner;
                candidate.LeaseExpiresAt = now + leaseDuration;
                candidate.LeaseGeneration = observedGeneration + 1;
                candidate.AttemptCount += 1;
                candidate.StartedAt ??= now;
                candidate.UpdatedAt = now;
                candidate.NextAttemptAt = null;

                // The generation acts as the concurrency token: another worker's claim
                // bumps it, so this save fails rather than stealing an owned job.
                db.Entry(candidate).Property(job => job.LeaseGeneration)
                    .OriginalValue = observedGeneration;

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    return candidate;
                }
                catch (DbUpdateConcurrencyException)
                {
                    foreach (var entry in db.ChangeTracker.Entries<TagJob>().ToList())
                    {
                        await entry.ReloadAsync(cancellationToken);
                    }
                }
            }

            return null;
        }

        public async Task<bool> RenewLeaseAsync(
            Guid id,
            string leaseOwner,
            DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            var rows = await db.TagJobs
                .Where(job =>
                    job.Id == id
                    && job.LeaseOwner == leaseOwner
                    && job.Status == TagJobStatus.Running)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(job => job.LeaseExpiresAt, expiresAt),
                    cancellationToken);

            return rows > 0;
        }

        public async Task<bool> UpdateAsync(
            Guid id,
            Action<TagJob> mutate,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(mutate);

            var job = await db.TagJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
            if (job == null)
            {
                return false;
            }

            mutate(job);
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<int> ReleaseExpiredLeasesAsync(
            DateTime now,
            CancellationToken cancellationToken = default) =>
            await db.TagJobs
                .Where(job =>
                    job.Status == TagJobStatus.Running
                    && job.LeaseExpiresAt != null
                    && job.LeaseExpiresAt <= now)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(job => job.Status, TagJobStatus.Queued)
                        .SetProperty(job => job.Phase, TagJobPhase.None)
                        .SetProperty(job => job.LeaseOwner, (string?)null)
                        .SetProperty(job => job.LeaseExpiresAt, (DateTime?)null)
                        .SetProperty(job => job.Progress, 0d)
                        .SetProperty(job => job.UpdatedAt, now),
                    cancellationToken);
    }
}
