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
using Listenarr.Domain.Audiobooks.Conversion;

namespace Listenarr.Application.Audiobooks.Conversion
{
    /// <summary>
    /// Stopping and clearing jobs, as opposed to running them. Split from the queue
    /// itself so neither file has to be read whole to follow the other.
    /// </summary>
    public sealed partial class ConversionQueueService
    {
        public async Task<JobControlResult> CancelAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var job = await repository.GetAsync(jobId, cancellationToken);
            if (job == null)
            {
                return new JobControlResult(
                    JobControlOutcome.NotFound,
                    "That conversion no longer exists.");
            }

            if (job.Status.IsTerminal())
            {
                return new JobControlResult(
                    JobControlOutcome.AlreadyTerminal,
                    "That conversion has already finished.");
            }

            // Moving the row off Running is the whole mechanism: RenewLeaseAsync only
            // renews a Running job, so the worker's next heartbeat fails, and it takes
            // the lost-lease path that abandons the encode and deletes its scratch file.
            // The lease fields are deliberately left as they are - clearing them would
            // race that check rather than reinforce it.
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var updated = await repository.UpdateAsync(jobId, target =>
            {
                target.Status = ConversionJobStatus.Cancelled;
                target.Phase = ConversionJobPhase.None;
                target.ActiveDeduplicationKey = null;
                target.NextAttemptAt = null;
                target.CanRetry = true;
                target.CompletedAt = now;
                target.UpdatedAt = now;
            }, cancellationToken);

            if (!updated)
            {
                return new JobControlResult(
                    JobControlOutcome.NotFound,
                    "That conversion no longer exists.");
            }

            logger.LogInformation(
                "Cancelled conversion {JobId} for audiobook {AudiobookId}",
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
                // Already gone is the outcome the caller wanted, but say so rather than
                // report a success that removed nothing.
                return new JobControlResult(
                    JobControlOutcome.NotFound,
                    "That conversion no longer exists.");
            }

            if (job.Status.IsActive())
            {
                return new JobControlResult(
                    JobControlOutcome.StillActive,
                    "That conversion is still running. Cancel it first.");
            }

            // A conversion never removes its sources before publishing, so the kept encode
            // is only ever a saved re-run and never the book's only copy. Deleting the row
            // leaves the scratch file with no job to belong to, which is exactly what the
            // sweeper collects on its next pass.
            var removed = await repository.DeleteAsync(jobId, cancellationToken);
            if (!removed)
            {
                return new JobControlResult(
                    JobControlOutcome.NotFound,
                    "That conversion no longer exists.");
            }

            logger.LogInformation(
                "Dismissed conversion {JobId} for audiobook {AudiobookId}{KeptEncode}",
                jobId,
                job.AudiobookId,
                string.IsNullOrWhiteSpace(job.VerifiedOutputPath)
                    ? string.Empty
                    : "; its kept encode will be swept");

            await broadcaster.BroadcastAsync("ConversionJobDismissed", new
            {
                jobId = job.Id.ToString(),
                audiobookId = job.AudiobookId
            }, cancellationToken);

            return JobControlResult.Done();
        }
    }
}
