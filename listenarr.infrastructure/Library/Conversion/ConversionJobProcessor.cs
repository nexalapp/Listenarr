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

        private async Task<ExecutionOutcome> ExecuteAsync(
            ConversionJob job,
            IServiceProvider services,
            IConversionQueueService queue,
            CancellationToken cancellationToken)
        {
            var audiobookRepository = services.GetRequiredService<IAudiobookRepository>();
            var audiobook = await audiobookRepository.GetByIdAsync(job.AudiobookId);
            if (audiobook == null)
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.SourceUnreadable,
                    "That audiobook no longer exists.");
            }

            var sources = CollectSourceFiles(audiobook);
            if (sources.Count == 0)
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.SourceUnreadable,
                    "This book no longer has any MP3 files to convert.");
            }

            var semanticsResolver = services.GetRequiredService<IFileSystemSemanticsResolver>();
            var pathComparer = await ResolvePathComparerAsync(
                semanticsResolver,
                audiobook,
                cancellationToken);

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.Probing, 0, cancellationToken);

            var ffmpegService = services.GetRequiredService<IFfmpegService>();
            var planning = await BuildPlanAsync(
                audiobook,
                sources.Select(s => s.File).ToList(),
                ffmpegService,
                pathComparer,
                cancellationToken);

            if (!planning.Success)
            {
                return ExecutionOutcome.Failed(planning.FailureKind, planning.Error!);
            }

            var plan = planning.Plan!;
            var settings = await services.GetRequiredService<IConfigurationService>()
                .GetApplicationSettingsAsync();

            var destinationPath = await BuildDestinationPathAsync(
                audiobook,
                planning.Tags!,
                services,
                settings);

            if (destinationPath == null)
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.Unknown,
                    "The converted file's destination could not be determined because this book has no known folder.");
            }

            // The encode writes outside the library, and publication then imports the
            // result the same way a completed download is imported. The library
            // filesystem carries no scratch namespace by design, and a partly written
            // encode sitting next to the book is exactly what that rule exists to
            // prevent — a reader cannot tell it from a real file.
            var scratchDirectory = services.GetRequiredService<IApplicationPathService>()
                .ResolveFromConfig("conversion");
            Directory.CreateDirectory(scratchDirectory);

            var scratchPath = Path.Combine(scratchDirectory, $"conversion-{job.Id:N}.m4b");

            var converter = services.GetRequiredService<IAudiobookConverter>();
            var progress = new Progress<ConversionProgress>(report =>
            {
                // Fire-and-forget: progress that could not be persisted must never stall
                // or fail the encode reporting it.
                _ = queue.ReportProgressAsync(
                    job.Id,
                    ConversionJobPhase.Encoding,
                    report.Fraction * 100,
                    CancellationToken.None);
            });

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.Encoding, 0, cancellationToken);

            ConversionResult result;
            try
            {
                result = await converter.ConvertAsync(
                    new ConversionRequest(plan, scratchPath, planning.Tags!),
                    progress,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // A cancelled encode leaves a partial file behind; nothing has been
                // published, so removing it is safe and keeps the library folder clean.
                TryDeleteScratch(scratchPath);
                throw;
            }

            if (!result.Success)
            {
                TryDeleteScratch(scratchPath);
                return ExecutionOutcome.Failed(result.FailureKind, result.Message ?? "The conversion failed.");
            }

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.Publishing, 95, cancellationToken);

            var publication = await PublishConvertedFileAsync(
                audiobook,
                scratchPath,
                destinationPath,
                services.GetRequiredService<IFileMover>(),
                services.GetRequiredService<IAudiobookFileService>(),
                cancellationToken);

            if (!publication.Success)
            {
                TryDeleteScratch(scratchPath);
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.OutputRejected,
                    publication.Error!);
            }

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.RetiringSources, 98, cancellationToken);

            var warning = await RetireSourcesAsync(
                audiobook,
                sources.Select(s => new SourceFileReference(s.File.Id, s.FullPath)).ToList(),
                settings,
                services.GetRequiredService<IAudiobookFileRepository>(),
                services.GetRequiredService<IFileSystem>(),
                cancellationToken);

            return ExecutionOutcome.Succeeded(
                publication.OutputPath!,
                result.ChapterCount,
                audiobook.Title,
                warning);
        }

        /// <summary>
        /// Where the M4B lands: the book's own directory, named from the configured file
        /// naming pattern. Conversion replaces a book's files; it does not relocate it.
        /// </summary>
        private static async Task<string?> BuildDestinationPathAsync(
            Audiobook audiobook,
            AudioMetadata tags,
            IServiceProvider services,
            ApplicationSettings settings)
        {
            if (string.IsNullOrWhiteSpace(audiobook.BasePath))
            {
                return null;
            }

            var namingService = services.GetRequiredService<IFileNamingService>();

            string fileName;
            try
            {
                fileName = namingService.ApplyNamingPattern(
                    settings.FileNamingPattern,
                    tags,
                    treatAsFilename: true);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                fileName = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = FileUtils.SafeFileName(
                    audiobook.Title is { Length: > 0 } title ? title : $"audiobook-{audiobook.Id}");
            }

            return await Task.FromResult(
                Path.GetFullPath(Path.Combine(audiobook.BasePath, fileName + ".m4b")));
        }

        /// <summary>The book's MP3 files, with their paths resolved.</summary>
        private static List<(AudiobookFile File, string FullPath)> CollectSourceFiles(Audiobook audiobook)
        {
            var results = new List<(AudiobookFile, string)>();
            foreach (var file in audiobook.Files ?? [])
            {
                if (string.IsNullOrWhiteSpace(file.Path)
                    || !string.Equals(Path.GetExtension(file.Path), ".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fullPath = ResolveFullPath(audiobook, file);
                if (fullPath != null)
                {
                    results.Add((file, fullPath));
                }
            }

            return results;
        }

        /// <summary>
        /// Ordering has to use the source filesystem's own case semantics, or two files
        /// differing only in case would be treated as one on a case-sensitive share.
        /// </summary>
        private static async Task<StringComparer> ResolvePathComparerAsync(
            IFileSystemSemanticsResolver resolver,
            Audiobook audiobook,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(audiobook.BasePath))
                {
                    var resolution = await resolver.ResolveAsync(
                        audiobook.BasePath,
                        FileSystemCaseSensitivityMode.Auto,
                        cancellationToken);
                    if (resolution.Semantics.Comparer is { } comparer)
                    {
                        return comparer;
                    }
                }
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                // Fall through to the conservative default below.
            }

            // Ordinal treats more paths as distinct, which is the safe direction: it can
            // list a duplicate, never silently drop a chapter.
            return StringComparer.Ordinal;
        }

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
