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
using Listenarr.Application.Audiobooks.Repair;
using Listenarr.Domain.Audiobooks.Repair;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Conversion
{
    /// <summary>
    /// The enhancement stage of a repair: every source file through the model in bounded
    /// chunks, reassembled losslessly, checkpointed per file.
    /// </summary>
    public sealed partial class ConversionJobProcessor
    {
        /// <summary>Lossless and half a WAV's size; hours of intermediates at 600 MB/h add up.</summary>
        private const string IntermediateExtension = ".flac";

        /// <summary>
        /// An intermediate may not drift from its source by more than this, or the
        /// chapter marks computed from the source durations stop landing where they say.
        /// </summary>
        private static readonly TimeSpan EnhancedDurationTolerance = TimeSpan.FromSeconds(0.5);

        private sealed record Intermediate(string Path, TimeSpan Duration);

        private sealed record EnhanceOutcome(
            bool Success,
            IReadOnlyDictionary<string, Intermediate>? Intermediates,
            ConversionFailureKind FailureKind,
            string? Error)
        {
            public static EnhanceOutcome Done(IReadOnlyDictionary<string, Intermediate> intermediates) =>
                new(true, intermediates, ConversionFailureKind.None, null);

            public static EnhanceOutcome Failed(ConversionFailureKind kind, string error) =>
                new(false, null, kind, error);
        }

        private async Task<EnhanceOutcome> EnhanceSourcesAsync(
            ConversionJob job,
            Audiobook audiobook,
            ConversionPlan plan,
            IAudioEnhancer enhancer,
            string repairDirectory,
            ApplicationSettings settings,
            IServiceProvider services,
            IConversionQueueService queue,
            CancellationToken cancellationToken)
        {
            var checkpoints = services.GetRequiredService<IRepairCheckpointRepository>();
            var gate = services.GetRequiredService<EnhancerGate>();
            var ffmpeg = await services.GetRequiredService<IFfmpegService>().GetFfmpegPathAsync();
            if (string.IsNullOrEmpty(ffmpeg))
            {
                return EnhanceOutcome.Failed(ConversionFailureKind.EncoderUnavailable, "No ffmpeg is installed.");
            }

            var enhancerStatus = await enhancer.GetStatusAsync(cancellationToken);
            var pipelineVersion = enhancerStatus.PipelineVersion ?? "unknown";
            var existing = (await checkpoints.GetForJobAsync(job.Id, cancellationToken))
                .ToDictionary(checkpoint => checkpoint.AudiobookFileId);

            var filesById = (audiobook.Files ?? []).ToDictionary(
                file => AudiobookFilePaths.ResolveFullPath(audiobook, file) ?? string.Empty,
                file => file,
                StringComparer.Ordinal);

            var totalDuration = plan.OrderedSources.Sum(source => source.Duration.TotalSeconds);
            double enhancedDuration = 0;
            var completedFiles = 0;
            var intermediates = new Dictionary<string, Intermediate>(StringComparer.Ordinal);

            foreach (var source in plan.OrderedSources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!filesById.TryGetValue(source.FullPath, out var file))
                {
                    return EnhanceOutcome.Failed(
                        ConversionFailureKind.SourceUnreadable,
                        $"A planned source is no longer registered: {LogRedaction.SanitizeFilePath(source.FullPath)}");
                }

                var intermediatePath = Path.Combine(repairDirectory, $"src-{file.Id}{IntermediateExtension}");

                // A checkpoint is honored only when it can prove three things: the source
                // is byte-for-byte the one that was enhanced, the intermediate is intact,
                // and the model has not changed under it.
                if (existing.TryGetValue(file.Id, out var checkpoint)
                    && CheckpointHolds(checkpoint, source.FullPath, intermediatePath, pipelineVersion))
                {
                    intermediates[source.FullPath] = new Intermediate(intermediatePath, source.Duration);
                    enhancedDuration += source.Duration.TotalSeconds;
                    completedFiles++;
                    continue;
                }

                var outcome = await EnhanceOneFileAsync(
                    job, source, file.Id, intermediatePath, enhancer, gate, ffmpeg, settings,
                    pipelineVersion, queue, enhancedDuration, totalDuration, cancellationToken);
                if (!outcome.Success)
                {
                    return EnhanceOutcome.Failed(outcome.FailureKind, outcome.Error!);
                }

                var sourceInfo = new FileInfo(source.FullPath);
                await checkpoints.UpsertAsync(
                    new RepairFileCheckpoint
                    {
                        JobId = job.Id,
                        AudiobookFileId = file.Id,
                        SourcePath = source.FullPath,
                        SourceLength = sourceInfo.Length,
                        SourceLastWriteUtc = sourceInfo.LastWriteTimeUtc,
                        EnhancedPath = intermediatePath,
                        EnhancedLength = new FileInfo(intermediatePath).Length,
                        EnhancerVersion = pipelineVersion,
                        CompletedAtUtc = DateTime.UtcNow
                    },
                    CancellationToken.None);

                intermediates[source.FullPath] = new Intermediate(intermediatePath, source.Duration);
                enhancedDuration += source.Duration.TotalSeconds;
                completedFiles++;
                await RecordCompletedFilesAsync(services, job.Id, completedFiles, pipelineVersion);
                await ReportEnhancementProgressAsync(queue, job.Id, enhancedDuration, totalDuration);
            }

            return EnhanceOutcome.Done(intermediates);
        }

        private static bool CheckpointHolds(
            RepairFileCheckpoint checkpoint,
            string sourcePath,
            string intermediatePath,
            string pipelineVersion)
        {
            if (!string.Equals(checkpoint.SourcePath, sourcePath, StringComparison.Ordinal)
                || !string.Equals(checkpoint.EnhancedPath, intermediatePath, StringComparison.Ordinal)
                || !string.Equals(checkpoint.EnhancerVersion, pipelineVersion, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                var source = new FileInfo(sourcePath);
                var intermediate = new FileInfo(intermediatePath);
                return source.Exists
                    && intermediate.Exists
                    && source.Length == checkpoint.SourceLength
                    && source.LastWriteTimeUtc == checkpoint.SourceLastWriteUtc
                    && intermediate.Length == checkpoint.EnhancedLength;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private async Task<EnhanceOutcome> EnhanceOneFileAsync(
            ConversionJob job,
            ConversionSource source,
            int fileId,
            string intermediatePath,
            IAudioEnhancer enhancer,
            EnhancerGate gate,
            string ffmpeg,
            ApplicationSettings settings,
            string pipelineVersion,
            IConversionQueueService queue,
            double enhancedDurationSoFar,
            double totalDuration,
            CancellationToken cancellationToken)
        {
            // The in-flight file's chunks live beside a manifest naming exactly what they
            // are chunks OF. A crash loses the chunk being worked, nothing more; a source
            // or model change invalidates the lot.
            var workDirectory = intermediatePath + ".wip";
            Directory.CreateDirectory(workDirectory);
            var chunkLength = await ResumableChunkLengthAsync(workDirectory, source, pipelineVersion);
            var chunks = RepairChunkPlanner.Plan(source.Duration, chunkLength);
            if (chunks.Count == 0)
            {
                return EnhanceOutcome.Failed(
                    ConversionFailureKind.SourceUnreadable,
                    $"A source decodes to nothing: {LogRedaction.SanitizeFilePath(source.FullPath)}");
            }

            var manifest = await LoadOrStartChunkManifestAsync(
                workDirectory, source, pipelineVersion, chunkLength, chunks.Count);

            // Say so now: a chunk takes the best part of half an hour, and until the first
            // one finishes the job would otherwise still read "Probing".
            await ReportEnhancementProgressAsync(queue, job.Id, enhancedDurationSoFar, totalDuration);

            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunkPath = Path.Combine(workDirectory, $"chunk-{chunk.Index:D4}.wav");
                if (manifest.Done.Contains(chunk.Index) && File.Exists(chunkPath) && new FileInfo(chunkPath).Length > 0)
                {
                    // Counted, or a resumed book reads 1% done with half its chunks finished.
                    enhancedDurationSoFar += chunk.Length.TotalSeconds;
                    continue;
                }

                var originalChunk = Path.Combine(workDirectory, $"orig-{chunk.Index:D4}.wav");
                var enhancedChunk = Path.Combine(workDirectory, $"enh-{chunk.Index:D4}.wav");
                try
                {
                    await ExtractChunkAsync(ffmpeg, source.FullPath, chunk, originalChunk, cancellationToken);

                    try
                    {
                        using (await gate.EnterAsync(cancellationToken))
                        {
                            await enhancer.EnhanceChunkAsync(originalChunk, enhancedChunk, chunk.Length, cancellationToken);
                        }
                    }
                    catch (Exception exception) when (exception is TimeoutException or InvalidOperationException)
                    {
                        return EnhanceOutcome.Failed(ConversionFailureKind.EnhanceFailed, exception.Message);
                    }
                    catch (EnhancerUnavailableException exception)
                    {
                        return EnhanceOutcome.Failed(ConversionFailureKind.EnhancerUnavailable, exception.Message);
                    }

                    // The music guard: the model deletes a score it mistakes for noise,
                    // and deleting something loud is the signature. The original chunk is
                    // already decoded on the same sample grid, so splicing it back costs a
                    // rename.
                    var useOriginal = false;
                    if (settings.RepairMusicProtectionEnabled)
                    {
                        var originalRms = await MeasureRmsDbAsync(ffmpeg, originalChunk, cancellationToken);
                        var enhancedRms = await MeasureRmsDbAsync(ffmpeg, enhancedChunk, cancellationToken);
                        if (originalRms != null && enhancedRms != null)
                        {
                            useOriginal = ChunkEnergyGuard.Decide(originalRms.Value, enhancedRms.Value)
                                == ChunkOutcome.UseOriginal;
                        }
                    }

                    if (useOriginal)
                    {
                        logger.LogInformation(
                            "Repair {JobId}: chunk {Chunk} of {File} kept its original audio — the model removed a loud passage, which is what it does to music",
                            job.Id,
                            chunk.Index,
                            fileId);
                        File.Move(originalChunk, chunkPath, overwrite: true);
                        File.Delete(enhancedChunk);
                    }
                    else
                    {
                        File.Move(enhancedChunk, chunkPath, overwrite: true);
                        File.Delete(originalChunk);
                    }
                }
                finally
                {
                    TryDeleteScratch(originalChunk);
                    TryDeleteScratch(enhancedChunk);
                }

                manifest.Done.Add(chunk.Index);
                await SaveChunkManifestAsync(workDirectory, manifest);

                enhancedDurationSoFar += chunk.Length.TotalSeconds;
                await ReportEnhancementProgressAsync(queue, job.Id, enhancedDurationSoFar, totalDuration);
            }

            // Reassemble losslessly and hold the result against the source's own clock:
            // half a second of drift here is a chapter mark landing in the wrong sentence
            // for the rest of the book.
            await ConcatChunksAsync(
                ffmpeg,
                chunks.Select(chunk => Path.Combine(workDirectory, $"chunk-{chunk.Index:D4}.wav")).ToList(),
                intermediatePath,
                cancellationToken);

            var measured = await MeasureDecodedDurationAsync(intermediatePath, cancellationToken);
            if (measured == null || (measured.Value - source.Duration).Duration() > EnhancedDurationTolerance)
            {
                TryDeleteScratch(intermediatePath);
                return EnhanceOutcome.Failed(
                    ConversionFailureKind.EnhanceFailed,
                    $"The enhanced form of {Path.GetFileName(source.FullPath)} runs {measured?.ToString() ?? "unreadably"} "
                    + $"against the source's {source.Duration}; the chapter marks would drift, so it was discarded.");
            }

            Directory.Delete(workDirectory, recursive: true);
            return EnhanceOutcome.Done(EmptyIntermediates);
        }

        private static readonly IReadOnlyDictionary<string, Intermediate> EmptyIntermediates =
            new Dictionary<string, Intermediate>(StringComparer.Ordinal);

        // ----------------------------------------------------------------- chunk pieces

        private async Task RecordCompletedFilesAsync(
            IServiceProvider services,
            Guid jobId,
            int completedFiles,
            string pipelineVersion)
        {
            try
            {
                await services.GetRequiredService<IConversionJobRepository>().UpdateAsync(jobId, job =>
                {
                    job.CompletedSourceFiles = completedFiles;
                    job.EnhancerVersion = pipelineVersion;
                });
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not record file progress for repair {JobId}", jobId);
            }
        }

        private async Task ReportEnhancementProgressAsync(
            IConversionQueueService queue,
            Guid jobId,
            double enhancedSeconds,
            double totalSeconds)
        {
            try
            {
                // Enhancement owns 0–90 of the bar; encode, verify and publish keep their
                // usual last ten points.
                var progress = totalSeconds <= 0 ? 0 : Math.Clamp(enhancedSeconds / totalSeconds, 0, 1) * 90;
                await queue.ReportProgressAsync(jobId, ConversionJobPhase.Enhancing, progress, CancellationToken.None);
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not record enhancement progress for repair {JobId}", jobId);
            }
        }
    }
}
