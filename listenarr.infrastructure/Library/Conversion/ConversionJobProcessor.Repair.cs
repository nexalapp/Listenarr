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
using Listenarr.Application.Common.Exceptions;
using Listenarr.Domain.Audiobooks.Repair;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Conversion
{
    /// <summary>
    /// The repair lane: the same job table and machinery as conversion, driven by its own
    /// worker, because a repair lasting a day or two must not park every ordinary
    /// conversion behind it. The shared deduplication key still guarantees a book is
    /// never converted and repaired at once.
    /// </summary>
    public sealed partial class ConversionJobProcessor
    {
        public async Task RunRepairCycleAsync(CancellationToken cancellationToken)
        {
            using var shutdown = LinkToShutdown(ref cancellationToken);
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IConversionQueueService>();

            await SweepRepairScratchAsync(scope.ServiceProvider, queue, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                await queue.RecoverAbandonedJobsAsync(cancellationToken);

                var job = await queue.ClaimNextAsync(_leaseOwner, ConversionJobKind.Repair, cancellationToken);
                if (job == null)
                {
                    return;
                }

                await RunJobAsync(job, cancellationToken);
            }
        }

        private async Task<ExecutionOutcome> ExecuteRepairAsync(
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

            var enhancer = services.GetService<Listenarr.Application.Audiobooks.Repair.IAudioEnhancer>();
            if (enhancer == null || !await enhancer.IsAvailableAsync(cancellationToken))
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.EnhancerUnavailable,
                    "The enhancement model is not provisioned and ready.");
            }

            var sourceFiles = (audiobook.Files ?? [])
                .Where(file => RepairableSource.IsRepairable(file.Path))
                .ToList();
            if (sourceFiles.Count == 0)
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.SourceUnreadable,
                    "This book no longer has any audio files to repair.");
            }

            try
            {
                await services.GetRequiredService<IMoveQueueService>()
                    .EnsureFilesystemMutationAllowedAsync(job.AudiobookId, cancellationToken);
            }
            catch (ApplicationConflictException conflict)
            {
                return ExecutionOutcome.Failed(ConversionFailureKind.Transient, conflict.SafeDetail);
            }

            var pathComparer = await ResolvePathComparerAsync(
                services.GetRequiredService<IFileSystemSemanticsResolver>(),
                audiobook,
                cancellationToken);

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.Probing, 0, cancellationToken);

            var settings = await services.GetRequiredService<IConfigurationService>()
                .GetApplicationSettingsAsync();
            var ffmpegService = services.GetRequiredService<IFfmpegService>();

            // The plan over the ORIGINALS: it decides ordering, carries every embedded
            // chapter mark (an M4B's included), and resolves the tags. The repair target
            // policy is fixed by design — the transcode selectors would throw away the
            // treble the model is about to restore.
            var planning = await BuildPlanAsync(
                audiobook,
                sourceFiles,
                ffmpegService,
                services.GetRequiredService<AudiobookTagPlanner>(),
                TagCatalog.Reconcile(settings.TagMappings),
                await audiobookRepository.GetSeriesPositionStylesAsync(cancellationToken),
                pathComparer,
                cancellationToken,
                RepairPlanPolicy.SelectTarget);

            if (!planning.Success)
            {
                return ExecutionOutcome.Failed(planning.FailureKind, planning.Error!);
            }

            var destinationPath = await BuildDestinationPathAsync(audiobook, planning.Metadata!, services, settings);
            if (destinationPath == null)
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.Unknown,
                    "The repaired file's destination could not be determined because this book has no known folder.");
            }

            var scratchDirectory = services.GetRequiredService<IApplicationPathService>()
                .ResolveFromConfig("conversion");
            var repairDirectory = Path.Combine(scratchDirectory, $"repair-{job.Id:N}");
            Directory.CreateDirectory(repairDirectory);

            // Most of a repair's life: every source through the model, chunk by chunk,
            // with per-file checkpoints so a restart redoes one chunk and not a day.
            var enhanced = await EnhanceSourcesAsync(
                job,
                audiobook,
                planning.Plan!,
                enhancer,
                repairDirectory,
                settings,
                services,
                queue,
                cancellationToken);
            if (!enhanced.Success)
            {
                return ExecutionOutcome.Failed(enhanced.FailureKind, enhanced.Error!);
            }

            // The plan over the INTERMEDIATES: same order, same chapter marks (each
            // intermediate was verified to run within half a second of its source, so the
            // marks still land), with the sources swapped for the enhanced FLACs.
            var byOriginal = enhanced.Intermediates!;
            var repairedSources = planning.Plan!.OrderedSources
                .Select(source => source with
                {
                    FullPath = byOriginal[source.FullPath].Path,
                    Duration = byOriginal[source.FullPath].Duration,
                    BitRate = null,
                    SampleRate = RepairPlanPolicy.SampleRate,
                    Channels = 1,
                    EmbeddedChapters = null
                })
                .ToList();
            var repairedChapters = planning.Plan.Chapters
                .Select(chapter => chapter with { SourceFullPath = byOriginal[chapter.SourceFullPath].Path })
                .ToList();
            var plan = new ConversionPlan(
                repairedSources,
                repairedChapters,
                planning.Plan.TargetBitRate,
                planning.Plan.TargetSampleRate,
                planning.Plan.TargetChannels);

            var scratchPath = Path.Combine(scratchDirectory, $"conversion-{job.Id:N}.m4b");
            // The cover comes from the ORIGINALS: the intermediates are bare audio.
            var cover = await ResolveCoverForEncodeAsync(
                audiobook, planning.Plan!.OrderedSources, scratchDirectory, job.Id, settings, services, cancellationToken);
            var request = new ConversionRequest(plan, scratchPath, planning.Tags!, cover?.Path);
            var converter = services.GetRequiredService<IAudiobookConverter>();

            ConversionResult? result;
            try
            {
                result = await TryReuseVerifiedOutputAsync(job, request, queue, converter, cancellationToken);
                if (result == null)
                {
                    await queue.ReportProgressAsync(job.Id, ConversionJobPhase.Encoding, 91, cancellationToken);
                    try
                    {
                        result = await converter.ConvertAsync(request, progress: null, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        TryDeleteScratch(scratchPath);
                        throw;
                    }
                }
            }
            finally
            {
                DeleteScratchCover(cover);
            }

            if (!result.Success)
            {
                TryDeleteScratch(scratchPath);
                return ExecutionOutcome.Failed(result.FailureKind, result.Message ?? "The repair's encode failed.");
            }

            var tagged = await services.GetRequiredService<IAudiobookTagWriter>()
                .ApplyAsync(scratchPath, planning.Tags!, cancellationToken);
            if (!tagged.Success)
            {
                var kept = tagged.FailureKind == TagWriteFailureKind.OutputRejected
                    && TryKeepRejectedOutput(job.Id, scratchPath);
                if (!kept)
                {
                    TryDeleteScratch(scratchPath);
                }

                return ExecutionOutcome.Failed(
                    ConversionFailureKind.OutputRejected,
                    $"The repaired file's tags could not be written: {tagged.Message}");
            }

            // The encode is verified. Remember it BEFORE touching any original: from here
            // to publication the kept output is the book's best copy, and a crash in the
            // window resumes by publishing it.
            await RememberVerifiedOutputAsync(job, scratchPath, result, queue);

            var sources = planning.Plan.OrderedSources
                .Select(source => new SourceFileReference(
                    sourceFiles.First(file => string.Equals(
                        AudiobookFilePaths.ResolveFullPath(audiobook, file),
                        source.FullPath,
                        StringComparison.Ordinal)).Id,
                    source.FullPath))
                .ToList();

            // The one structural difference from conversion. A book already stored as
            // one M4B resolves its repaired output to the source's own path, and
            // conversion's publish-then-retire would overwrite the original and then
            // archive the fresh repair. Colliding sources retire FIRST — safe exactly
            // because the verified encode was recorded above and the originals land in
            // the archive.
            var rootFolders = await services.GetRequiredService<IRootFolderService>().GetAllAsync();
            var fileRepository = services.GetRequiredService<IAudiobookFileRepository>();
            var fileSystem = services.GetRequiredService<IFileSystem>();
            var colliding = sources
                .Where(source => pathComparer.Equals(source.FullPath, destinationPath))
                .ToList();
            string? collidingWarning = null;
            var archivedOriginals = new List<(string From, string To)>();
            if (colliding.Count > 0)
            {
                await queue.ReportProgressAsync(job.Id, ConversionJobPhase.RetiringSources, 94, cancellationToken);
                collidingWarning = await RetireSourcesAsync(
                    audiobook,
                    colliding,
                    settings,
                    rootFolders,
                    fileRepository,
                    fileSystem,
                    cancellationToken,
                    settings.RepairSourceDisposition,
                    archivedOriginals);
            }

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.Publishing, 95, cancellationToken);
            PublicationOutcome publication;
            try
            {
                publication = await PublishConvertedFileAsync(
                    job.Id,
                    audiobook,
                    scratchPath,
                    destinationPath,
                    services.GetRequiredService<IFileMover>(),
                    services.GetRequiredService<IAudiobookFileService>(),
                    cancellationToken);
            }
            catch
            {
                // The verified encode is already remembered; the retry publishes it.
                throw;
            }

            if (!publication.Success)
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.OutputRejected,
                    await RestoreArchivedOriginalsAsync(audiobook, archivedOriginals, publication.Error!, services));
            }

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.RetiringSources, 98, cancellationToken);
            var warning = await RetireSourcesAsync(
                audiobook,
                sources.Except(colliding).ToList(),
                settings,
                rootFolders,
                fileRepository,
                fileSystem,
                cancellationToken,
                settings.RepairSourceDisposition);

            // The intermediates and checkpoints have served; the published M4B is the
            // durable artifact now.
            await services.GetRequiredService<IRepairCheckpointRepository>()
                .RemoveForJobAsync(job.Id, CancellationToken.None);
            TryDeleteDirectory(repairDirectory);

            // The book's audio changed, so everything derived from it is stale: queue a
            // fresh probe and a fresh audit the same way a scan would.
            await QueuePostRepairWorkAsync(audiobook.Id, services);

            return ExecutionOutcome.Succeeded(
                publication.OutputPath!,
                result.ChapterCount,
                audiobook.Title,
                collidingWarning ?? warning);
        }

        /// <summary>
        /// Repair scratch: a directory per job (chunked intermediates), spared while its
        /// job is active and, for a failed-but-retryable job, within a retention window —
        /// the intermediates represent days of model time, which is worth more disk
        /// patience than an encode's seven. Preview artifacts live a day.
        /// </summary>
        internal static readonly TimeSpan RepairCheckpointRetention = TimeSpan.FromDays(14);
        internal static readonly TimeSpan RepairPreviewRetention = TimeSpan.FromHours(24);

        private async Task SweepRepairScratchAsync(
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
                foreach (var directory in Directory.EnumerateDirectories(scratchDirectory, "repair-*"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(directory);
                    if (!Guid.TryParseExact(name["repair-".Length..], "N", out var jobId))
                    {
                        continue;
                    }

                    var job = await queue.GetJobAsync(jobId, cancellationToken);
                    if (job != null && job.Status.IsActive())
                    {
                        continue;
                    }

                    if (job is { Status: ConversionJobStatus.Failed, CanRetry: true }
                        && now - (job.CompletedAt ?? job.UpdatedAt ?? job.EnqueuedAt) < RepairCheckpointRetention)
                    {
                        continue;
                    }

                    TryDeleteDirectory(directory);
                    await services.GetRequiredService<IRepairCheckpointRepository>()
                        .RemoveForJobAsync(jobId, CancellationToken.None);
                    logger.LogInformation("Removed the repair scratch left by job {JobId}", jobId);
                }

                var previewRoot = Path.Combine(scratchDirectory, "repair-previews");
                if (Directory.Exists(previewRoot))
                {
                    foreach (var directory in Directory.EnumerateDirectories(previewRoot))
                    {
                        if (now - Directory.GetLastWriteTimeUtc(directory) > RepairPreviewRetention)
                        {
                            TryDeleteDirectory(directory);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not sweep repair scratch");
            }
        }

        /// <summary>A repaired book is a changed book: re-probe and re-audit, as a scan would.</summary>
        private async Task QueuePostRepairWorkAsync(int audiobookId, IServiceProvider services)
        {
            try
            {
                var prober = services.GetService<Listenarr.Application.Audiobooks.Quality.IAudioQualityService>();
                if (prober != null)
                {
                    // Manual, so it goes ahead of the scan-time probes: a repair was
                    // approved by someone, and this probe is what tells them whether it
                    // worked — the book stays flagged for repair until it runs. Behind
                    // a day's downloads it ran eight hours after the repair.
                    await prober.EnqueueAsync(audiobookId, Domain.Audiobooks.Tagging.TagTrigger.Manual, CancellationToken.None);
                }

                var auditor = services.GetService<Listenarr.Application.Audiobooks.Audit.IAudioAuditService>();
                if (auditor != null)
                {
                    await auditor.EnqueueAsync(audiobookId, Domain.Audiobooks.Tagging.TagTrigger.Automatic, CancellationToken.None);
                }
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not queue post-repair work for audiobook {AudiobookId}", audiobookId);
            }
        }

        private void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not remove {Directory}", LogRedaction.SanitizeFilePath(directory));
            }
        }
    }
}
