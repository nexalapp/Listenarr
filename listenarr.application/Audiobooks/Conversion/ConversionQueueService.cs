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
using Listenarr.Domain.Audiobooks.Conversion;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Conversion
{
    /// <summary>
    /// The durable conversion queue.
    ///
    /// Enqueueing is cheap and always returns: the decision about whether a book is
    /// worth converting is made here, but the conversion itself belongs to a worker.
    /// </summary>
    public sealed class ConversionQueueService(
        IConversionJobRepository repository,
        IAudiobookRepository audiobookRepository,
        IConfigurationService configurationService,
        IAudiobookConverter converter,
        IHubBroadcaster broadcaster,
        TimeProvider timeProvider,
        ILogger<ConversionQueueService> logger) : IConversionQueueService
    {
        /// <summary>
        /// How long a claim is good for before another worker may take the job. Long
        /// enough that an hour-long encode on a slow share is not stolen mid-run, and
        /// the worker heartbeats well inside it.
        /// </summary>
        public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

        /// <summary>How long a finished job stays visible in Activity.</summary>
        private static readonly TimeSpan TerminalVisibility = TimeSpan.FromHours(12);

        /// <summary>
        /// A failure the operator has to act on is not worth retrying on a timer, so
        /// these end the job immediately with the reason shown.
        /// </summary>
        private static bool IsWorthRetrying(ConversionFailureKind kind) => kind switch
        {
            ConversionFailureKind.EncoderUnavailable => false,
            ConversionFailureKind.SourceUnreadable => false,
            ConversionFailureKind.EncodeFailed => false,
            ConversionFailureKind.OutputRejected => false,
            _ => true
        };

        public async Task<ConversionEnqueueResult> EnqueueAsync(
            int audiobookId,
            ConversionTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.NotFound,
                    Reason: "That audiobook no longer exists.");
            }

            // A manual request is an explicit instruction and overrides the setting; the
            // setting only governs whether an import queues one on its own.
            if (trigger == ConversionTrigger.Automatic)
            {
                var settings = await configurationService.GetApplicationSettingsAsync();
                if (!settings.ConvertMp3ToM4b)
                {
                    return new ConversionEnqueueResult(
                        ConversionEnqueueOutcome.Disabled,
                        Reason: "Automatic MP3 to M4B conversion is switched off.");
                }
            }

            var convertible = CountConvertibleFiles(audiobook);
            if (convertible == 0)
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.NothingToConvert,
                    Reason: "This book has no MP3 files to convert.");
            }

            // Check for an encoder before writing a row. Queueing without one only
            // produces a job that fails the moment a worker picks it up.
            if (!await converter.IsAvailableAsync(cancellationToken))
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.EncoderUnavailable,
                    Reason: "No ffmpeg encoder is installed, so conversion is unavailable.");
            }

            var existing = await repository.GetActiveForAudiobookAsync(audiobookId, cancellationToken);
            if (existing != null)
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.AlreadyQueued,
                    existing.Id,
                    "This book is already queued for conversion.");
            }

            var job = new ConversionJob
            {
                AudiobookId = audiobookId,
                Trigger = trigger,
                SourceFileCount = convertible,
                ActiveDeduplicationKey = ConversionJob.BuildDeduplicationKey(audiobookId),
                EnqueuedAt = timeProvider.GetUtcNow().UtcDateTime
            };

            var stored = await repository.AddAsync(job, cancellationToken);
            if (stored == null)
            {
                // The unique index rejected it: a concurrent caller won the race, which is
                // the same outcome as finding an existing job above.
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.AlreadyQueued,
                    Reason: "This book is already queued for conversion.");
            }

            logger.LogInformation(
                "Queued conversion {JobId} for audiobook {AudiobookId} ({FileCount} file(s), {Trigger})",
                stored.Id,
                audiobookId,
                convertible,
                trigger);

            await BroadcastAsync(stored, cancellationToken);
            return new ConversionEnqueueResult(ConversionEnqueueOutcome.Queued, stored.Id);
        }

        public async Task<ConversionEnqueueResult> RetryAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var job = await repository.GetAsync(jobId, cancellationToken);
            if (job == null)
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.NotFound,
                    Reason: "That conversion job no longer exists.");
            }

            if (job.Status.IsActive())
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.AlreadyQueued,
                    job.Id,
                    "That conversion is already running.");
            }

            // Retrying re-runs the conversion from the top, so the attempt counter starts
            // over: the previous attempts were against whatever the problem was, and the
            // operator has presumably addressed it.
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var updated = await repository.UpdateAsync(jobId, target =>
            {
                target.Status = ConversionJobStatus.Queued;
                target.Phase = ConversionJobPhase.None;
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
                target.ActiveDeduplicationKey = ConversionJob.BuildDeduplicationKey(target.AudiobookId);
                target.EnqueuedAt = now;
            }, cancellationToken);

            if (!updated)
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.NotFound,
                    Reason: "That conversion job no longer exists.");
            }

            var refreshed = await repository.GetAsync(jobId, cancellationToken);
            if (refreshed != null)
            {
                await BroadcastAsync(refreshed, cancellationToken);
            }

            return new ConversionEnqueueResult(ConversionEnqueueOutcome.Queued, jobId);
        }

        public Task<ConversionJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            repository.GetAsync(jobId, cancellationToken);

        public Task<ConversionJob?> GetActiveJobForAudiobookAsync(
            int audiobookId,
            CancellationToken cancellationToken = default) =>
            repository.GetActiveForAudiobookAsync(audiobookId, cancellationToken);

        public Task<IReadOnlyList<ConversionJob>> GetVisibleJobsAsync(
            CancellationToken cancellationToken = default) =>
            repository.GetVisibleAsync(
                timeProvider.GetUtcNow().UtcDateTime - TerminalVisibility,
                cancellationToken);

        public async Task<ConversionJob?> ClaimNextAsync(
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
            ConversionJobPhase phase,
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

        public async Task RecordVerifiedOutputAsync(
            Guid jobId,
            string outputPath,
            long outputLength,
            int chapterCount,
            CancellationToken cancellationToken = default)
        {
            await repository.UpdateAsync(jobId, job =>
            {
                job.VerifiedOutputPath = outputPath;
                job.VerifiedOutputLength = outputLength;
                job.ChapterCount = chapterCount;
            }, cancellationToken);

            logger.LogInformation(
                "Kept the verified encode for conversion {JobId} so a retry need not repeat it",
                jobId);
        }

        public async Task ClearVerifiedOutputAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            await repository.UpdateAsync(jobId, job =>
            {
                job.VerifiedOutputPath = null;
                job.VerifiedOutputLength = null;
            }, cancellationToken);
        }

        public async Task CompleteAsync(
            Guid jobId,
            string outputPath,
            int chapterCount,
            CancellationToken cancellationToken = default)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await repository.UpdateAsync(jobId, job =>
            {
                job.Status = ConversionJobStatus.Completed;
                job.Phase = ConversionJobPhase.None;
                job.Progress = 100;
                job.OutputPath = FileUtils.NormalizeStoredPath(outputPath);
                job.ChapterCount = chapterCount;
                job.Error = null;
                job.FailureKind = null;
                job.CanRetry = false;
                job.CompletedAt = now;
                job.LeaseOwner = null;
                job.LeaseExpiresAt = null;
                // The kept encode has been moved into the library, so the scratch path
                // it named no longer holds anything.
                job.VerifiedOutputPath = null;
                job.VerifiedOutputLength = null;
                // Clearing the key releases the unique index so the book can be
                // converted again later without colliding with this row.
                job.ActiveDeduplicationKey = null;
            }, cancellationToken);

            var job = await repository.GetAsync(jobId, cancellationToken);
            if (job != null)
            {
                logger.LogInformation(
                    "Conversion {JobId} completed for audiobook {AudiobookId} with {Chapters} chapter(s)",
                    jobId,
                    job.AudiobookId,
                    chapterCount);
                await BroadcastAsync(job, cancellationToken);
            }
        }

        public async Task FailAsync(
            Guid jobId,
            ConversionFailureKind failureKind,
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
                    job.Status = ConversionJobStatus.RetryScheduled;
                    job.Phase = ConversionJobPhase.None;
                    job.Progress = 0;
                    // Exponential backoff so a share that is briefly away is not hammered.
                    job.NextAttemptAt = now + TimeSpan.FromMinutes(Math.Pow(2, job.AttemptCount));
                    job.CanRetry = true;
                }
                else
                {
                    job.Status = ConversionJobStatus.Failed;
                    job.Phase = ConversionJobPhase.None;
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
                    "Conversion {JobId} for audiobook {AudiobookId} failed ({Kind}): {Error}",
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
                    "Returned {Count} abandoned conversion job(s) to the queue",
                    released);
            }
        }

        /// <summary>
        /// How many of a book's files a conversion would actually fold in. A book that is
        /// already a single M4B has nothing to gain, and one with no MP3s has nothing to
        /// convert.
        /// </summary>
        private static int CountConvertibleFiles(Audiobook audiobook)
        {
            var files = audiobook.Files;
            if (files == null || files.Count == 0)
            {
                return 0;
            }

            return files.Count(file =>
                !string.IsNullOrWhiteSpace(file.Path)
                && string.Equals(
                    Path.GetExtension(file.Path),
                    ".mp3",
                    StringComparison.OrdinalIgnoreCase));
        }

        private async Task BroadcastAsync(ConversionJob job, CancellationToken cancellationToken)
        {
            try
            {
                await broadcaster.BroadcastAsync("ConversionJobUpdate", new
                {
                    jobId = job.Id.ToString(),
                    audiobookId = job.AudiobookId,
                    status = job.Status.ToString(),
                    phase = job.Phase.ToString(),
                    progress = job.Progress,
                    chapterCount = job.ChapterCount,
                    sourceFileCount = job.SourceFileCount,
                    error = job.Error,
                    failureKind = job.FailureKind,
                    canRetry = job.CanRetry,
                    trigger = job.Trigger.ToString()
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                // A durable state change that could not be broadcast is still durable.
                logger.LogDebug(ex, "Could not broadcast conversion job {JobId}", job.Id);
            }
        }
    }
}
