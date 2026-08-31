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

namespace Listenarr.Infrastructure.Library.Tagging
{
    /// <summary>
    /// Runs queued tag writes, one at a time.
    ///
    /// Deliberately serial for the same reason conversion is: a rewrite pulls a whole
    /// book across a NAS share and pushes it back, and running several would make every
    /// one of them slower while starving the imports and scans sharing that share.
    /// </summary>
    public sealed partial class TagJobProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<TagJobProcessor> logger) : ITagJobProcessor
    {
        /// <summary>
        /// Identifies this process as a lease holder. Includes a per-start GUID so a
        /// restarted process does not look like the one that abandoned a job.
        /// </summary>
        private readonly string _leaseOwner =
            $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

        /// <summary>Renewed well inside the lease so a slow rewrite is never stolen.</summary>
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(2);

        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<ITagQueueService>();

            // Reclaim anything a previous process left mid-flight before looking for new
            // work, so a restart resumes rather than stalls. This matters more here than
            // for a conversion: an interrupted job may have removed a library file and be
            // holding its only replacement.
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

        private async Task RunJobAsync(TagJob job, CancellationToken cancellationToken)
        {
            // A fresh scope per job: a rewrite of a long book can take minutes, and
            // holding one DbContext open across that would keep a connection and a change
            // tracker alive for the whole run.
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var queue = services.GetRequiredService<ITagQueueService>();

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
                    await queue.CompleteAsync(job.Id, outcome.TagsWritten, CancellationToken.None);
                    await NotifyAsync(services, outcome);
                }
                else
                {
                    await queue.FailAsync(
                        job.Id,
                        outcome.FailureKind,
                        outcome.Error ?? "The tag write failed.",
                        CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown. Leave the job Running: its lease will expire and another
                // process will reclaim it, which is what makes a restart resumable.
                logger.LogInformation(
                    "Tag write {JobId} interrupted by shutdown; its lease will be reclaimed",
                    job.Id);
                throw;
            }
            catch (OperationCanceledException)
            {
                // The lease was lost, so another worker owns this job now. Writing a
                // failure here would overwrite that worker's progress.
                logger.LogWarning("Tag write {JobId} stopped because its lease was lost", job.Id);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Tag write {JobId} failed unexpectedly", job.Id);
                await queue.FailAsync(
                    job.Id,
                    TagWriteFailureKind.Unknown,
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
        /// when the lease is lost, which stops the rewrite rather than letting two workers
        /// replace the same library file.
        /// </summary>
        private async Task KeepLeaseAliveAsync(
            Guid jobId,
            ITagQueueService queue,
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
                        logger.LogWarning("Lost the lease on tag write {JobId}; stopping it", jobId);
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
                logger.LogWarning(ex, "Tag write lease heartbeat failed for {JobId}", jobId);
            }
        }

        /// <summary>
        /// Remove scratch files left by jobs that are no longer active.
        ///
        /// <para>
        /// A crash mid-rewrite leaves one behind, and for a real book it is hundreds of
        /// megabytes. Nothing else would ever remove it, so the config volume would fill
        /// one interrupted run at a time.
        /// </para>
        /// <para>
        /// A file a job records as a pending publication is never swept, however old the
        /// job is and whatever state it reached. The library file it replaces has already
        /// been removed, so that scratch file is the book's only copy — deleting it on a
        /// retention timer would lose the book.
        /// </para>
        /// </summary>
        private async Task SweepOrphanedScratchFilesAsync(
            IServiceProvider services,
            ITagQueueService queue,
            CancellationToken cancellationToken)
        {
            try
            {
                var scratchDirectory = services.GetRequiredService<IApplicationPathService>()
                    .ResolveFromConfig("tagging");
                if (!Directory.Exists(scratchDirectory))
                {
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(scratchDirectory, "tagging-*.m4b"))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var name = Path.GetFileNameWithoutExtension(file);
                    var identifier = name["tagging-".Length..];
                    var separator = identifier.IndexOf('-', StringComparison.Ordinal);
                    if (separator > 0)
                    {
                        identifier = identifier[..separator];
                    }

                    if (!Guid.TryParseExact(identifier, "N", out var jobId))
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

                    if (job != null
                        && !string.IsNullOrWhiteSpace(job.PendingOutputPath)
                        && PathsMatch(job.PendingOutputPath, file))
                    {
                        // The library is missing the file this replaces. It stays until a
                        // retry publishes it, or an operator resolves the job by hand.
                        logger.LogWarning(
                            "Tag write {JobId} still holds the only copy of a library file at {Path}; it will not be swept",
                            jobId,
                            LogRedaction.SanitizeFilePath(file));
                        continue;
                    }

                    TryDeleteScratch(file);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not sweep orphaned tag-write scratch files");
            }
        }

        private static bool PathsMatch(string? left, string? right) =>
            !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.Ordinal);

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
                    "Could not remove the tag-write scratch file {Path}",
                    LogRedaction.SanitizeFilePath(scratchPath));
            }
        }

        private static async Task NotifyAsync(IServiceProvider services, ExecutionOutcome outcome)
        {
            try
            {
                var toasts = services.GetRequiredService<IToastService>();
                var title = outcome.AudiobookTitle is { Length: > 0 } name ? name : "Audiobook";

                if (outcome.TagsWritten == 0)
                {
                    // Worth saying: an operator who asked for this and saw nothing happen
                    // would otherwise assume it failed.
                    await toasts.PublishToastAsync(
                        "info",
                        "Tags already correct",
                        $"{title} already carries the tags the mapping produces, so nothing was rewritten.");
                }
                else
                {
                    await toasts.PublishToastAsync(
                        "success",
                        "Tags written",
                        $"{title}: {outcome.TagsWritten} tag(s) written into {outcome.FilesWritten} file(s).");
                }
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                // A toast that failed to publish does not undo a completed tag write.
            }
        }

        private static bool IsNonFatal(Exception ex) =>
            ex is not OperationCanceledException
            && ex is not OutOfMemoryException
            && ex is not StackOverflowException;

        private sealed record ExecutionOutcome(
            bool Success,
            int TagsWritten,
            int FilesWritten,
            TagWriteFailureKind FailureKind,
            string? Error,
            string? AudiobookTitle = null)
        {
            public static ExecutionOutcome Succeeded(
                int tagsWritten,
                int filesWritten,
                string? audiobookTitle) =>
                new(true, tagsWritten, filesWritten, TagWriteFailureKind.None, null, audiobookTitle);

            public static ExecutionOutcome Failed(TagWriteFailureKind kind, string error) =>
                new(false, 0, 0, kind, error);
        }
    }
}
