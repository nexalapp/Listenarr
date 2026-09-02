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
using Microsoft.Extensions.Logging;
namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// Stopping and clearing jobs, as opposed to running them. Split from the queue
    /// itself so neither file has to be read whole to follow the other.
    /// </summary>
    public sealed partial class TagQueueService
    {
        /// <summary>
        /// A job past the point of removing the original is the only record of where the
        /// replacement is. Until it publishes, nothing may clear it away.
        /// </summary>
        private static bool HoldsOnlyCopy(TagJob job) =>
            !string.IsNullOrWhiteSpace(job.PendingOutputPath);

        private const string OnlyCopyRefusal =
            "This tag write is holding the book's only copy of the file, because the "
            + "original was removed to make room for it. Retry it so the replacement is "
            + "published; it cannot be cancelled or dismissed until then.";

        public async Task<JobControlResult> CancelAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var job = await repository.GetAsync(jobId, cancellationToken);
            if (job == null)
            {
                return new JobControlResult(
                    JobControlOutcome.NotFound,
                    "That tag write no longer exists.");
            }

            if (job.Status.IsTerminal())
            {
                return new JobControlResult(
                    JobControlOutcome.AlreadyTerminal,
                    "That tag write has already finished.");
            }

            if (HoldsOnlyCopy(job))
            {
                return new JobControlResult(JobControlOutcome.HoldsOnlyCopy, OnlyCopyRefusal);
            }

            // As with a conversion, moving the row off Running is what stops the worker:
            // its next heartbeat cannot renew a lease on a job that is no longer Running.
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var updated = await repository.UpdateAsync(jobId, target =>
            {
                target.Status = TagJobStatus.Cancelled;
                target.Phase = TagJobPhase.None;
                target.ActiveDeduplicationKey = null;
                target.NextAttemptAt = null;
                target.CanRetry = true;
                target.CompletedAt = now;
            }, cancellationToken);

            if (!updated)
            {
                return new JobControlResult(
                    JobControlOutcome.NotFound,
                    "That tag write no longer exists.");
            }

            logger.LogInformation(
                "Cancelled tag write {JobId} for audiobook {AudiobookId}",
                jobId,
                job.AudiobookId);

            var refreshed = await repository.GetAsync(jobId, cancellationToken);
            if (refreshed != null)
            {
                await BroadcastAsync(refreshed, cancellationToken);
            }

            return JobControlResult.Done();
        }

        public async Task<JobControlResult> DismissAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var job = await repository.GetAsync(jobId, cancellationToken);
            if (job == null)
            {
                return new JobControlResult(
                    JobControlOutcome.NotFound,
                    "That tag write no longer exists.");
            }

            if (job.Status.IsActive())
            {
                return new JobControlResult(
                    JobControlOutcome.StillActive,
                    "That tag write is still running. Cancel it first.");
            }

            // The row is what stops the sweeper taking the rewrite, so dismissing a job
            // that still holds the only copy would hand the book's only file to the next
            // sweep. A failed publication is recoverable; this would not be.
            if (HoldsOnlyCopy(job))
            {
                return new JobControlResult(JobControlOutcome.HoldsOnlyCopy, OnlyCopyRefusal);
            }

            var removed = await repository.DeleteAsync(jobId, cancellationToken);
            if (!removed)
            {
                return new JobControlResult(
                    JobControlOutcome.NotFound,
                    "That tag write no longer exists.");
            }

            logger.LogInformation(
                "Dismissed tag write {JobId} for audiobook {AudiobookId}",
                jobId,
                job.AudiobookId);

            await broadcaster.BroadcastAsync("TagJobDismissed", new
            {
                jobId = job.Id.ToString(),
                audiobookId = job.AudiobookId
            }, cancellationToken);

            return JobControlResult.Done();
        }
    }
}
