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
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// The durable tag-writing queue.
    ///
    /// Enqueueing is cheap and always returns: the decision about whether a book is worth
    /// rewriting is made here, but the rewrite itself belongs to a worker.
    /// </summary>
    public sealed partial class TagQueueService(
        ITagJobRepository repository,
        IAudiobookRepository audiobookRepository,
        IConfigurationService configurationService,
        IAudiobookTagWriter tagWriter,
        IHubBroadcaster broadcaster,
        TimeProvider timeProvider,
        ILogger<TagQueueService> logger) : ITagQueueService
    {
        /// <summary>
        /// How long a claim is good for before another worker may take the job. A rewrite
        /// is a straight copy rather than an encode, so it is far shorter than a
        /// conversion's — but still generous enough for a book-sized file over a share.
        /// </summary>
        public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

        /// <summary>How long a finished job stays visible in Activity.</summary>
        private static readonly TimeSpan TerminalVisibility = TimeSpan.FromHours(12);

        /// <summary>
        /// A failure the operator has to act on is not worth retrying on a timer, so
        /// these end the job immediately with the reason shown.
        /// </summary>
        private static bool IsWorthRetrying(TagWriteFailureKind kind) => kind switch
        {
            TagWriteFailureKind.WriterUnavailable => false,
            TagWriteFailureKind.SourceUnreadable => false,
            TagWriteFailureKind.WriteFailed => false,
            TagWriteFailureKind.OutputRejected => false,
            _ => true
        };

        public async Task<TagEnqueueResult> EnqueueAsync(
            int audiobookId,
            TagTrigger trigger,
            IReadOnlyCollection<string>? selectedTags = null,
            IReadOnlyDictionary<string, string>? values = null,
            CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.NotFound,
                    Reason: "That audiobook no longer exists.");
            }

            // A manual request is an explicit instruction and overrides the setting; the
            // setting only governs whether an import queues one on its own.
            if (trigger == TagTrigger.Automatic)
            {
                var settings = await configurationService.GetApplicationSettingsAsync();
                if (!settings.WriteMetadataTags)
                {
                    return new TagEnqueueResult(
                        TagEnqueueOutcome.Disabled,
                        Reason: "Automatic metadata tag writing is switched off.");
                }
            }

            var taggable = CountTaggableFiles(audiobook);
            if (taggable == 0)
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.NothingToTag,
                    Reason: "This book has no M4B files to write tags into.");
            }

            // Check for a writer before writing a row. Queueing without one only produces
            // a job that fails the moment a worker picks it up.
            if (!await tagWriter.IsAvailableAsync(cancellationToken))
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.WriterUnavailable,
                    Reason: "No ffmpeg is installed, so tags cannot be written.");
            }

            var existing = await repository.GetActiveForAudiobookAsync(audiobookId, cancellationToken);
            if (existing != null)
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.AlreadyQueued,
                    existing.Id,
                    "This book is already queued for tag writing.");
            }

            var job = new TagJob
            {
                AudiobookId = audiobookId,
                Trigger = trigger,
                FileCount = taggable,
                SelectedTagsJson = SerializeSelection(selectedTags),
                OverriddenValuesJson = SerializeValues(values),
                ActiveDeduplicationKey = TagJob.BuildDeduplicationKey(audiobookId),
                EnqueuedAt = timeProvider.GetUtcNow().UtcDateTime
            };

            var stored = await repository.AddAsync(job, cancellationToken);
            if (stored == null)
            {
                // The unique index rejected it: a concurrent caller won the race, which is
                // the same outcome as finding an existing job above.
                return new TagEnqueueResult(
                    TagEnqueueOutcome.AlreadyQueued,
                    Reason: "This book is already queued for tag writing.");
            }

            logger.LogInformation(
                "Queued tag write {JobId} for audiobook {AudiobookId} ({FileCount} file(s), {Trigger})",
                stored.Id,
                audiobookId,
                taggable,
                trigger);

            await BroadcastAsync(stored, cancellationToken);
            return new TagEnqueueResult(TagEnqueueOutcome.Queued, stored.Id);
        }

        public async Task<TagEnqueueResult> RetryAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var job = await repository.GetAsync(jobId, cancellationToken);
            if (job == null)
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.NotFound,
                    Reason: "That tag-writing job no longer exists.");
            }

            if (job.Status.IsActive())
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.AlreadyQueued,
                    job.Id,
                    "That tag write is already running.");
            }

            // Retrying re-runs from the top, so the attempt counter starts over: the
            // previous attempts were against whatever the problem was, and the operator
            // has presumably addressed it. The tag selection is kept — it was the
            // operator's choice for this book and re-running should honour it.
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var updated = await repository.UpdateAsync(jobId, target =>
            {
                target.Status = TagJobStatus.Queued;
                target.Phase = TagJobPhase.None;
                target.Progress = 0;
                target.Error = null;
                target.FailureKind = null;
                target.CanRetry = true;
                target.AttemptCount = 0;
                target.NextAttemptAt = null;
                target.LeaseOwner = null;
                target.LeaseExpiresAt = null;
                target.CompletedAt = null;
                target.StartedAt = null;
                target.TagsWritten = 0;
                // The selection and the typed values are kept: they were the operator's
                // decisions about this book, and re-running should honour them.
                target.ActiveDeduplicationKey = TagJob.BuildDeduplicationKey(target.AudiobookId);
                target.EnqueuedAt = now;
            }, cancellationToken);

            if (!updated)
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.NotFound,
                    Reason: "That tag-writing job no longer exists.");
            }

            var refreshed = await repository.GetAsync(jobId, cancellationToken);
            if (refreshed != null)
            {
                await BroadcastAsync(refreshed, cancellationToken);
            }

            return new TagEnqueueResult(TagEnqueueOutcome.Queued, jobId);
        }

        public Task<TagJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            repository.GetAsync(jobId, cancellationToken);

        public Task<TagJob?> GetActiveJobForAudiobookAsync(
            int audiobookId,
            CancellationToken cancellationToken = default) =>
            repository.GetActiveForAudiobookAsync(audiobookId, cancellationToken);

        public Task<IReadOnlyList<TagJob>> GetVisibleJobsAsync(
            CancellationToken cancellationToken = default) =>
            repository.GetVisibleAsync(
                timeProvider.GetUtcNow().UtcDateTime - TerminalVisibility,
                cancellationToken);

        public async Task<TagJob?> ClaimNextAsync(
            string leaseOwner,
            CancellationToken cancellationToken = default)
        {
            var claimed = await repository.ClaimNextAsync(
                leaseOwner,
                timeProvider.GetUtcNow().UtcDateTime,
                LeaseDuration,
                cancellationToken);

            if (claimed != null)
            {
                await BroadcastAsync(claimed, cancellationToken);
            }

            return claimed;
        }

        public Task<bool> HeartbeatAsync(
            Guid jobId,
            string leaseOwner,
            CancellationToken cancellationToken = default) =>
            repository.RenewLeaseAsync(
                jobId,
                leaseOwner,
                timeProvider.GetUtcNow().UtcDateTime + LeaseDuration,
                cancellationToken);

        public async Task ReportProgressAsync(
            Guid jobId,
            TagJobPhase phase,
            double progress,
            CancellationToken cancellationToken = default)
        {
            var clamped = Math.Clamp(progress, 0, 100);
            await repository.UpdateAsync(jobId, job =>
            {
                job.Phase = phase;
                job.Progress = clamped;
            }, cancellationToken);

            var job = await repository.GetAsync(jobId, cancellationToken);
            if (job != null)
            {
                await BroadcastAsync(job, cancellationToken);
            }
        }

        public async Task RecordPendingPublicationAsync(
            Guid jobId,
            int fileId,
            string outputPath,
            long outputLength,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            await repository.UpdateAsync(jobId, job =>
            {
                job.PendingFileId = fileId;
                job.PendingOutputPath = outputPath;
                job.PendingOutputLength = outputLength;
                job.PendingDestinationPath = FileUtils.NormalizeStoredPath(destinationPath);
            }, cancellationToken);

            logger.LogInformation(
                "Tag write {JobId} holds a verified rewrite for file {FileId} awaiting publication",
                jobId,
                fileId);
        }

        public async Task ClearPendingPublicationAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            await repository.UpdateAsync(jobId, job =>
            {
                job.PendingFileId = null;
                job.PendingOutputPath = null;
                job.PendingOutputLength = null;
                job.PendingDestinationPath = null;
            }, cancellationToken);
        }

        public async Task CompleteAsync(
            Guid jobId,
            int tagsWritten,
            CancellationToken cancellationToken = default)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await repository.UpdateAsync(jobId, job =>
            {
                job.Status = TagJobStatus.Completed;
                job.Phase = TagJobPhase.None;
                job.Progress = 100;
                job.TagsWritten = tagsWritten;
                job.Error = null;
                job.FailureKind = null;
                job.CanRetry = false;
                job.CompletedAt = now;
                job.LeaseOwner = null;
                job.LeaseExpiresAt = null;
                // Every rewrite this job produced is in the library now, so the scratch
                // path it named no longer holds anything.
                job.PendingFileId = null;
                job.PendingOutputPath = null;
                job.PendingOutputLength = null;
                job.PendingDestinationPath = null;
                // Clearing the key releases the unique index so the book can be tagged
                // again later without colliding with this row.
                job.ActiveDeduplicationKey = null;
            }, cancellationToken);

            var job = await repository.GetAsync(jobId, cancellationToken);
            if (job != null)
            {
                logger.LogInformation(
                    "Tag write {JobId} completed for audiobook {AudiobookId}: {Tags} tag(s) written",
                    jobId,
                    job.AudiobookId,
                    tagsWritten);
                await BroadcastAsync(job, cancellationToken);
            }
        }

        public async Task FailAsync(
            Guid jobId,
            TagWriteFailureKind failureKind,
            string error,
            CancellationToken cancellationToken = default)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await repository.UpdateAsync(jobId, job =>
            {
                job.Error = error;
                job.FailureKind = failureKind.ToString();
                job.LeaseOwner = null;
                job.LeaseExpiresAt = null;

                var retryable = IsWorthRetrying(failureKind) && job.AttemptCount < job.MaxAttempts;
                if (retryable)
                {
                    job.Status = TagJobStatus.RetryScheduled;
                    job.Phase = TagJobPhase.None;
                    job.Progress = 0;
                    // Exponential backoff so a share that is briefly away is not hammered.
                    job.NextAttemptAt = now + TimeSpan.FromMinutes(Math.Pow(2, job.AttemptCount));
                    job.CanRetry = true;
                }
                else
                {
                    job.Status = TagJobStatus.Failed;
                    job.Phase = TagJobPhase.None;
                    job.CompletedAt = now;
                    job.NextAttemptAt = null;
                    // An operator can still retry by hand once they have addressed the
                    // cause; what stops here is retrying on a timer.
                    job.CanRetry = true;
                    job.ActiveDeduplicationKey = null;
                }
            }, cancellationToken);

            var job = await repository.GetAsync(jobId, cancellationToken);
            if (job != null)
            {
                logger.LogWarning(
                    "Tag write {JobId} for audiobook {AudiobookId} failed ({Kind}): {Error}",
                    jobId,
                    job.AudiobookId,
                    failureKind,
                    error);
                await BroadcastAsync(job, cancellationToken);
            }
        }

        public async Task RecoverAbandonedJobsAsync(CancellationToken cancellationToken = default)
        {
            var released = await repository.ReleaseExpiredLeasesAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);

            if (released > 0)
            {
                logger.LogInformation(
                    "Returned {Count} abandoned tag-writing job(s) to the queue",
                    released);
            }
        }

        /// <summary>
        /// How many of a book's files a tag write would touch. MP3s are excluded because
        /// ID3 cannot carry the atom this exists to write; those books are converted first.
        /// </summary>
        private static int CountTaggableFiles(Audiobook audiobook) =>
            audiobook.Files?.Count(file => TaggableFile.IsTaggable(file.Path)) ?? 0;

        private async Task BroadcastAsync(TagJob job, CancellationToken cancellationToken)
        {
            try
            {
                await broadcaster.BroadcastAsync("TagJobUpdate", new
                {
                    jobId = job.Id.ToString(),
                    audiobookId = job.AudiobookId,
                    status = job.Status.ToString(),
                    phase = job.Phase.ToString(),
                    progress = job.Progress,
                    fileCount = job.FileCount,
                    tagsWritten = job.TagsWritten,
                    error = job.Error,
                    failureKind = job.FailureKind,
                    canRetry = job.CanRetry,
                    trigger = job.Trigger.ToString()
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                // A durable state change that could not be broadcast is still durable.
                logger.LogDebug(ex, "Could not broadcast tag job {JobId}", job.Id);
            }
        }
    }
}
