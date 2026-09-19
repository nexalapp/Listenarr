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
using Listenarr.Application.Audiobooks.Audit;
using Listenarr.Application.Audiobooks.Chapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Tagging
{
    /// <summary>
    /// The chapter kind of job: the same shape as a tag write — read, rewrite to scratch,
    /// verify, publish through the file mover — with the rewrite done by the chapter
    /// rewriter from the plan the job carries rather than by the tag writer from the
    /// planner. Nothing here decides a chapter; that was decided at enqueue time and
    /// previewed.
    /// </summary>
    public sealed partial class TagJobProcessor
    {
        private async Task<ExecutionOutcome> ExecuteChapterRepairAsync(
            TagJob job,
            Audiobook audiobook,
            IReadOnlyList<AudiobookFile> files,
            bool resumedOne,
            int filesWritten,
            IServiceProvider services,
            ITagQueueService queue,
            CancellationToken cancellationToken)
        {
            var plans = TagQueueService.DeserializeChapterPlans(job.ChapterPlanJson);
            if (plans.Count == 0)
            {
                return ExecutionOutcome.Failed(
                    TagWriteFailureKind.SourceUnreadable,
                    "This job carries no chapter plan, so there is nothing to write.");
            }

            var writer = services.GetRequiredService<IAudiobookTagWriter>();
            var rewriter = services.GetRequiredService<IChapterRewriter>();
            var scratchDirectory = services.GetRequiredService<IApplicationPathService>()
                .ResolveFromConfig("tagging");
            Directory.CreateDirectory(scratchDirectory);

            var chaptersWritten = 0;
            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = files[index];
                if (job.PendingFileId == file.Id && resumedOne)
                {
                    continue;
                }

                if (!plans.TryGetValue(file.Id, out var plan))
                {
                    continue;
                }

                var basePercent = 100.0 * index / files.Count;
                await queue.ReportProgressAsync(job.Id, TagJobPhase.Reading, basePercent, cancellationToken);

                var fullPath = AudiobookFilePaths.ResolveFullPath(audiobook, file);
                if (fullPath == null || !File.Exists(fullPath))
                {
                    return ExecutionOutcome.Failed(
                        TagWriteFailureKind.SourceUnreadable,
                        $"File is missing: {LogRedaction.SanitizeFilePath(fullPath ?? file.Path)}");
                }

                AudiobookFileTags existing;
                try
                {
                    existing = await writer.ReadAsync(fullPath, cancellationToken);
                }
                catch (FfmpegException ex)
                {
                    return ExecutionOutcome.Failed(TagWriteFailureKind.SourceUnreadable, ex.Message);
                }

                var scratchPath = Path.Combine(scratchDirectory, $"tagging-{job.Id:N}-{file.Id}.m4b");
                await queue.ReportProgressAsync(job.Id, TagJobPhase.Writing, basePercent, cancellationToken);

                TagWriteResult result;
                try
                {
                    result = await rewriter.RewriteAsync(
                        new ChapterRewriteRequest(fullPath, scratchPath, plan, existing),
                        BuildProgress(job.Id, basePercent, 100.0 / files.Count),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    TryDeleteScratch(scratchPath);
                    throw;
                }

                if (!result.Success)
                {
                    TryDeleteScratch(scratchPath);
                    return ExecutionOutcome.Failed(result.FailureKind, result.Message ?? "The chapter rewrite failed.");
                }

                var publication = await ReplaceLibraryFileAsync(
                    job,
                    audiobook,
                    file,
                    fullPath,
                    scratchPath,
                    services,
                    queue,
                    cancellationToken);

                if (!publication.Success)
                {
                    return ExecutionOutcome.Failed(publication.FailureKind, publication.Error!);
                }

                logger.LogInformation(
                    "Chapter repair {JobId}: wrote {Count} chapter(s) from {Source} into {Path}",
                    job.Id,
                    plan.Chapters.Count,
                    plan.Source,
                    LogRedaction.SanitizeFilePath(fullPath));

                chaptersWritten += plan.Chapters.Count;
                filesWritten++;
            }

            return ExecutionOutcome.Succeeded(chaptersWritten, filesWritten, audiobook.Title);
        }
        /// <summary>
        /// The audit kind: listen and record. No scratch file, no publication — the
        /// outcome is six columns on the book.
        /// </summary>
        private async Task<ExecutionOutcome> ExecuteAudioAuditAsync(
            TagJob job,
            Audiobook audiobook,
            IServiceProvider services,
            ITagQueueService queue,
            CancellationToken cancellationToken)
        {
            await queue.ReportProgressAsync(job.Id, TagJobPhase.Reading, 5, cancellationToken);
            var auditor = services.GetRequiredService<IAudioAuditService>();
            try
            {
                var result = await auditor.AuditAsync(audiobook.Id, BuildProgress(job.Id, 0, 100), cancellationToken);
                logger.LogInformation(
                    "Audio audit {JobId}: {Verdict} — {Reason}",
                    job.Id,
                    result.Verdict,
                    result.Reason);
                return ExecutionOutcome.Succeeded(0, job.FileCount, audiobook.Title);
            }
            catch (InvalidOperationException ex)
            {
                return ExecutionOutcome.Failed(TagWriteFailureKind.WriterUnavailable, ex.Message);
            }
            catch (FfmpegException ex)
            {
                return ExecutionOutcome.Failed(TagWriteFailureKind.SourceUnreadable, ex.Message);
            }
        }
        /// <summary>The plan kind: work out the fix for the named files and store it on them.</summary>
        private async Task<ExecutionOutcome> ExecuteChapterPlanAsync(
            TagJob job,
            Audiobook audiobook,
            IServiceProvider services,
            ITagQueueService queue,
            CancellationToken cancellationToken)
        {
            await queue.ReportProgressAsync(job.Id, TagJobPhase.Reading, 5, cancellationToken);
            var repair = services.GetRequiredService<IChapterRepairService>();
            var scope = TagQueueService.DeserializeFileIds(job.SelectedFileIdsJson);
            var planned = await repair.PlanAsync(audiobook.Id, scope, cancellationToken);
            logger.LogInformation("Chapter planning {JobId}: {Planned} of {Files} file(s) have a fix", job.Id, planned, job.FileCount);
            return ExecutionOutcome.Succeeded(0, planned, audiobook.Title);
        }
    }
}
