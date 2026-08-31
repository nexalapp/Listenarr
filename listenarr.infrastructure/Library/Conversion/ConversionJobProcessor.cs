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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Conversion
{
    /// <summary>
    /// Runs queued conversions, one at a time.
    ///
    /// Deliberately serial: an encode saturates CPU and pulls a whole book across a NAS
    /// share, and running several would make every one of them slower while starving the
    /// imports and scans sharing that share.
    /// </summary>
    public sealed partial class ConversionJobProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<ConversionJobProcessor> logger) : IConversionJobProcessor
    {
        /// <summary>
        /// Identifies this process as a lease holder. Includes a per-start GUID so a
        /// restarted process does not look like the one that abandoned a job.
        /// </summary>
        private readonly string _leaseOwner =
            $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

        /// <summary>Renewed well inside the lease so a slow encode is never stolen.</summary>
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(2);

        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IConversionQueueService>();

            // Reclaim anything a previous process left mid-flight before looking for new
            // work, so a restart resumes rather than stalls.
            await queue.RecoverAbandonedJobsAsync(cancellationToken);

            await SweepOrphanedScratchFilesAsync(scope.ServiceProvider, queue, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var job = await queue.ClaimNextAsync(_leaseOwner, cancellationToken);
                if (job == null)
                {
                    return;
                }

                await RunJobAsync(job, cancellationToken);
            }
        }

        private async Task RunJobAsync(ConversionJob job, CancellationToken cancellationToken)
        {
            // A fresh scope per job: an encode can run for an hour, and holding one
            // DbContext open across that would keep a connection and a change tracker
            // alive for the whole run.
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var queue = services.GetRequiredService<IConversionQueueService>();

            using var heartbeat = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                heartbeat.Token);

            var heartbeatTask = KeepLeaseAliveAsync(job.Id, queue, heartbeat, cancellationToken);

            try
            {
                var outcome = await ExecuteAsync(job, services, queue, linked.Token);

                if (outcome.Success)
                {
                    await queue.CompleteAsync(
                        job.Id,
                        outcome.OutputPath!,
                        outcome.ChapterCount,
                        CancellationToken.None);

                    await NotifyAsync(services, outcome, job);
                }
                else
                {
                    await queue.FailAsync(
                        job.Id,
                        outcome.FailureKind,
                        outcome.Error ?? "The conversion failed.",
                        CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown. Leave the job Running: its lease will expire and another
                // process will reclaim it, which is what makes a restart resumable.
                logger.LogInformation(
                    "Conversion {JobId} interrupted by shutdown; its lease will be reclaimed",
                    job.Id);
                throw;
            }
            catch (OperationCanceledException)
            {
                // The lease was lost, so another worker owns this job now. Writing a
                // failure here would overwrite that worker's progress.
                logger.LogWarning(
                    "Conversion {JobId} stopped because its lease was lost",
                    job.Id);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Conversion {JobId} failed unexpectedly", job.Id);
                await queue.FailAsync(
                    job.Id,
                    ConversionFailureKind.Unknown,
                    ex.Message,
                    CancellationToken.None);
            }
            finally
            {
                await heartbeat.CancelAsync();
                try
                {
                    await heartbeatTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected: cancelling the heartbeat is how it is stopped.
                }
            }
        }

        /// <summary>
        /// Renew the lease until the job finishes. Cancels <paramref name="heartbeat"/>
        /// when the lease is lost, which stops the encode rather than letting two workers
        /// write the same output.
        /// </summary>
        private async Task KeepLeaseAliveAsync(
            Guid jobId,
            IConversionQueueService queue,
            CancellationTokenSource heartbeat,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!heartbeat.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(HeartbeatInterval, heartbeat.Token);

                    if (!await queue.HeartbeatAsync(jobId, _leaseOwner, cancellationToken))
                    {
                        logger.LogWarning("Lost the lease on conversion {JobId}; stopping it", jobId);
                        await heartbeat.CancelAsync();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal completion path.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Conversion lease heartbeat failed for {JobId}", jobId);
            }
        }

        /// <summary>
        /// Remove scratch files left by jobs that are no longer active.
        ///
        /// A crash mid-encode leaves one behind, and for a real book it is hundreds of
        /// megabytes. Nothing else would ever remove it, so the config volume would fill
        /// one interrupted conversion at a time.
        /// </summary>
        private async Task SweepOrphanedScratchFilesAsync(
            IServiceProvider services,
            IConversionQueueService queue,
            CancellationToken cancellationToken)
        {
            try
            {
                var scratchDirectory = services.GetRequiredService<IApplicationPathService>()
                    .ResolveFromConfig("conversion");
                if (!Directory.Exists(scratchDirectory))
                {
                    return;
                }

                var now = DateTime.UtcNow;

                foreach (var file in Directory.EnumerateFiles(scratchDirectory, "conversion-*.m4b"))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!Guid.TryParseExact(name["conversion-".Length..], "N", out var jobId))
                    {
                        continue;
                    }

                    // Only the job's own worker may remove its scratch file while it runs,
                    // so an active job's file is left alone even though it looks orphaned.
                    var job = await queue.GetJobAsync(jobId, cancellationToken);
                    if (job != null && job.Status.IsActive())
                    {
                        continue;
                    }

                    // A failed job may be holding a verified encode so a retry need not
                    // repeat it. That is worth real disk, but not indefinitely: a book
                    // nobody retries would otherwise keep its output forever.
                    if (job != null
                        && !string.IsNullOrWhiteSpace(job.VerifiedOutputPath)
                        && IsWithinKeptOutputRetention(job, now))
                    {
                        continue;
                    }

                    TryDeleteScratch(file);
                    if (job != null && !string.IsNullOrWhiteSpace(job.VerifiedOutputPath))
                    {
                        await queue.ClearVerifiedOutputAsync(jobId, cancellationToken);
                        logger.LogInformation(
                            "Dropped the kept encode for conversion {JobId} after {Days} day(s) unretried",
                            jobId,
                            KeptOutputRetention.TotalDays);
                    }
                    else
                    {
                        logger.LogInformation(
                            "Removed the scratch file left by conversion {JobId}",
                            jobId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not sweep orphaned conversion scratch files");
            }
        }

        /// <summary>
        /// How long a failed job may hold the encode it could not publish. Long enough
        /// that an operator fixing a mount or a permission gets the retry for free, short
        /// enough that an abandoned book does not keep a book-sized file indefinitely.
        /// </summary>
        internal static readonly TimeSpan KeptOutputRetention = TimeSpan.FromDays(7);

        internal static bool IsWithinKeptOutputRetention(ConversionJob job, DateTime now) =>
            now - (job.CompletedAt ?? job.UpdatedAt ?? job.EnqueuedAt) < KeptOutputRetention;

        private void TryDeleteScratch(string scratchPath)
        {
            try
            {
                if (File.Exists(scratchPath))
                {
                    File.Delete(scratchPath);
                }
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(
                    ex,
                    "Could not remove the conversion scratch file {Path}",
                    LogRedaction.SanitizeFilePath(scratchPath));
            }
        }

        private static async Task NotifyAsync(
            IServiceProvider services,
            ExecutionOutcome outcome,
            ConversionJob job)
        {
            try
            {
                var toasts = services.GetRequiredService<IToastService>();
                var title = outcome.AudiobookTitle is { Length: > 0 } name ? name : "Audiobook";

                if (outcome.Warning != null)
                {
                    await toasts.PublishToastAsync("warning", "Conversion finished", $"{title}: {outcome.Warning}");
                }
                else
                {
                    await toasts.PublishToastAsync(
                        "success",
                        "Conversion finished",
                        $"{title} is now a single M4B with {outcome.ChapterCount} chapter(s).");
                }
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                // A toast that failed to publish does not undo a completed conversion.
            }
        }

        private sealed record ExecutionOutcome(
            bool Success,
            string? OutputPath,
            int ChapterCount,
            ConversionFailureKind FailureKind,
            string? Error,
            string? AudiobookTitle = null,
            string? Warning = null)
        {
            public static ExecutionOutcome Succeeded(
                string outputPath,
                int chapterCount,
                string? audiobookTitle,
                string? warning) =>
                new(true, outputPath, chapterCount, ConversionFailureKind.None, null, audiobookTitle, warning);

            public static ExecutionOutcome Failed(ConversionFailureKind kind, string error) =>
                new(false, null, 0, kind, error);
        }
    }
}
