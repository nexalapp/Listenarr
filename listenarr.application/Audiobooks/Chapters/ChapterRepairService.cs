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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Application.Audiobooks.Transcription;
using Listenarr.Domain.Audiobooks.Chapters;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Chapters
{
    /// <summary>
    /// Plans a chapter repair per file from the sources the planner knows, keeps the
    /// plan on the file, and hands stored plans to the tag queue as one job for the book.
    ///
    /// <para>
    /// Planning is background work: it may fetch the edition and listen at every mark.
    /// So the interactive paths — the preview, the enqueue, the chapter page — only ever
    /// read a plan already stored, and queue a planning job for whatever has none. Only
    /// <see cref="PlanAsync"/> computes, and only the queue's worker calls it.
    /// </para>
    /// </summary>
    public sealed partial class ChapterRepairService(
        IAudiobookRepository audiobookRepository,
        IAudiobookTagWriter tagWriter,
        IChapterAtomRecovery atomRecovery,
        ITagQueueService tagQueue,
        IFileSystem fileSystem,
        IConfigurationService configurationService,
        ILogger<ChapterRepairService> logger,
        IAudnexusService? audnexus = null,
        ITranscriber? transcriber = null,
        TranscriptCache? transcripts = null,
        IAudiobookFileRepository? fileRepository = null) : IChapterRepairService
    {
        /// <summary>How much to listen to after each mark. Announcements come first, but not always in the first breath.</summary>
        public static readonly TimeSpan ListenWindow = TimeSpan.FromSeconds(10);

        /// <summary>More marks than this is not a book; it is a mistake, and an hour of CPU.</summary>
        public const int MaxMarksToHear = 400;

        public async Task<ChapterRepairPreview?> PreviewAsync(
            int audiobookId,
            IReadOnlyCollection<int>? fileIds = null,
            CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return null;
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            var previews = new List<ChapterRepairFilePreview>();
            var pending = new List<int>();
            foreach (var (file, fullPath, fileName) in FilesInScope(audiobook, fileIds))
            {
                var read = await ReadAsync(file, fullPath, fileName, cancellationToken);
                if (read.Preview != null)
                {
                    previews.Add(read.Preview);
                    continue;
                }

                var key = PlanKey(fullPath!, audiobook, settings.TranscriptionEnabled);
                var outcome = key != null ? ReadStoredPlan(file, key) : null;
                if (outcome == null)
                {
                    pending.Add(file.Id);
                }

                previews.Add(new ChapterRepairFilePreview(
                    file.Id,
                    fileName,
                    read.Health!.Health,
                    read.Health.Reason,
                    outcome?.Plan,
                    outcome?.Rejection,
                    PlanPending: outcome == null));
            }

            if (pending.Count > 0)
            {
                await tagQueue.EnqueueChapterPlanAsync(audiobookId, pending, TagTrigger.Automatic, cancellationToken);
            }

            return new ChapterRepairPreview(audiobookId, previews);
        }

        public async Task<IReadOnlyList<ChapterDescription>?> DescribeAsync(int audiobookId, CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return null;
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            var hasAsin = !string.IsNullOrWhiteSpace(audiobook.Asin);
            var descriptions = new List<ChapterDescription>();
            foreach (var (file, fullPath, fileName) in FilesInScope(audiobook, null))
            {
                if (fullPath == null)
                {
                    descriptions.Add(new ChapterDescription(file.Id, fileName, ChapterHealth.Unknown, null, [], null, TimeSpan.Zero, "The file is not readable from here."));
                    continue;
                }

                try
                {
                    var tags = await tagWriter.ReadAsync(fullPath, cancellationToken);
                    var health = ChapterHealthAnalyzer.Analyze(tags.Chapters, tags.Atoms, tags.Duration, Path.GetFileNameWithoutExtension(fileName));
                    ChapterPlanOutcome? proposal = null;
                    var planPending = false;
                    if (ChapterHealthSeverity.IsRepairableKind(health.Health))
                    {
                        var key = PlanKey(fullPath, audiobook, settings.TranscriptionEnabled);
                        proposal = key != null ? ReadStoredPlan(file, key) : null;
                        planPending = proposal == null;
                    }

                    descriptions.Add(new ChapterDescription(
                        file.Id,
                        fileName,
                        health.Health,
                        health.Reason,
                        tags.Chapters ?? [],
                        tags.Atoms,
                        tags.Duration,
                        null,
                        proposal != null
                            ? proposal.Plan != null
                            : ChapterHealthSeverity.LikelyRepairable(health.Health, tags.Atoms, hasAsin, settings.TranscriptionEnabled),
                        proposal,
                        planPending));
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    descriptions.Add(new ChapterDescription(file.Id, fileName, ChapterHealth.Unknown, null, [], null, TimeSpan.Zero, $"The file could not be read: {ex.Message}"));
                }
            }

            // A flagged file without a current plan gets one worked out in the
            // background, so the next look at this page has the answer.
            var pending = descriptions.Where(d => d.PlanPending).Select(d => d.FileId).ToList();
            if (pending.Count > 0)
            {
                await tagQueue.EnqueueChapterPlanAsync(audiobookId, pending, TagTrigger.Automatic, cancellationToken);
            }

            return descriptions;
        }

        public async Task<ChapterRepairPreview?> PlanAsync(
            int audiobookId,
            IReadOnlyCollection<int>? fileIds = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return null;
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            var files = FilesInScope(audiobook, fileIds).ToList();
            var previews = new List<ChapterRepairFilePreview>(files.Count);
            var unavailable = new List<string>();
            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (file, fullPath, fileName) = files[index];
                var read = await ReadAsync(file, fullPath, fileName, cancellationToken);
                if (read.Preview != null)
                {
                    previews.Add(read.Preview);
                    continue;
                }

                var health = read.Health!;
                var tags = read.Tags!;
                var key = PlanKey(fullPath!, audiobook, settings.TranscriptionEnabled);
                var outcome = key != null ? ReadStoredPlan(file, key) : null;
                if (outcome == null)
                {
                    var stage = new PlanProgress(progress, index, files.Count);
                    var attempt = health.Health is ChapterHealth.Oversegmented or ChapterHealth.GenericTitles
                        ? await PlanFromAnnouncementsAsync(audiobook, fullPath!, tags, health.Health, stage, cancellationToken)
                        : await PlanCorruptAsync(audiobook, fullPath!, tags, cancellationToken);
                    outcome = new ChapterPlanOutcome(attempt.Plan, attempt.Rejection?.Reason);

                    // An answer reached without a source that should have been asked is
                    // not the answer; it is not kept, and the job says why so it retries.
                    if (attempt.Unavailable != null)
                    {
                        unavailable.Add($"{fileName}: {attempt.Unavailable}");
                    }
                    else if (key != null)
                    {
                        await StorePlanAsync(file.Id, outcome, key, cancellationToken);
                    }
                }

                previews.Add(new ChapterRepairFilePreview(
                    file.Id,
                    fileName,
                    health.Health,
                    health.Reason,
                    outcome.Plan,
                    outcome.Rejection));
            }

            progress?.Report(1);
            if (unavailable.Count > 0)
            {
                throw new ChapterSourceUnavailableException(string.Join(" ", unavailable));
            }

            return new ChapterRepairPreview(audiobookId, previews);
        }

        public async Task<TagEnqueueResult> ReplanAsync(int audiobookId, IReadOnlyCollection<int>? fileIds = null, CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.NotFound, Reason: "That audiobook no longer exists.");
            }

            var ids = FilesInScope(audiobook, fileIds).Select(entry => entry.File.Id).ToList();
            if (ids.Count == 0)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.NothingToTag, Reason: "No file in scope has chapters to plan for.");
            }

            if (fileRepository != null)
            {
                await fileRepository.ClearChapterPlanAsync(ids, cancellationToken);
            }

            return await tagQueue.EnqueueChapterPlanAsync(audiobookId, ids, TagTrigger.Manual, cancellationToken);
        }

        public async Task<TagEnqueueResult> EnqueueAsync(
            int audiobookId,
            IReadOnlyCollection<int>? fileIds = null,
            CancellationToken cancellationToken = default)
        {
            var preview = await PreviewAsync(audiobookId, fileIds, cancellationToken);
            if (preview == null)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.NotFound);
            }

            var plans = preview.Files
                .Where(file => file.Plan != null)
                .ToDictionary(file => file.FileId, file => file.Plan!);

            if (plans.Count == 0)
            {
                var pending = preview.Files.Count(file => file.PlanPending);
                var reason = pending > 0
                    ? $"The fix for {pending} file(s) is still being worked out; it will show on the Chapters tab when it is ready."
                    : preview.Files.FirstOrDefault(file => file.Rejection != null)?.Rejection
                        ?? "No file in scope has a repairable chapter atom.";
                return new TagEnqueueResult(TagEnqueueOutcome.NothingToTag, Reason: reason);
            }

            return await tagQueue.EnqueueChapterRepairAsync(audiobookId, plans, TagTrigger.Manual, cancellationToken);
        }

        /// <summary>The book's M4Bs in scope, with where each is on disk (null when it is not here).</summary>
        private IEnumerable<(AudiobookFile File, string? FullPath, string FileName)> FilesInScope(Audiobook audiobook, IReadOnlyCollection<int>? fileIds)
        {
            var scope = fileIds == null ? null : new HashSet<int>(fileIds);
            foreach (var file in (audiobook.Files ?? [])
                         .Where(file => TaggableFile.IsTaggable(file.Path))
                         .Where(file => scope == null || scope.Contains(file.Id))
                         .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                var fullPath = AudiobookFilePaths.ResolveFullPath(audiobook, file);
                var fileName = Path.GetFileName(fullPath ?? file.Path ?? string.Empty);
                yield return (file, fullPath != null && fileSystem.FileExists(fullPath) ? fullPath : null, fileName);
            }
        }

        /// <summary>
        /// Read one file and judge it. Comes back with a finished preview when there is
        /// nothing to plan — unreadable, or not a repairable kind — else with the tags
        /// and verdict to plan from.
        /// </summary>
        private async Task<(ChapterRepairFilePreview? Preview, AudiobookFileTags? Tags, ChapterHealthReport? Health)> ReadAsync(
            AudiobookFile file,
            string? fullPath,
            string fileName,
            CancellationToken cancellationToken)
        {
            if (fullPath == null)
            {
                return (new ChapterRepairFilePreview(file.Id, fileName, ChapterHealth.Unknown, null, null, "The file is not readable from here."), null, null);
            }

            AudiobookFileTags tags;
            try
            {
                tags = await tagWriter.ReadAsync(fullPath, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                return (new ChapterRepairFilePreview(file.Id, fileName, ChapterHealth.Unknown, null, null, $"The file could not be read: {ex.Message}"), null, null);
            }

            var health = ChapterHealthAnalyzer.Analyze(
                tags.Chapters,
                tags.Atoms,
                tags.Duration,
                Path.GetFileNameWithoutExtension(fileName));

            if (!ChapterHealthSeverity.IsRepairableKind(health.Health))
            {
                return (new ChapterRepairFilePreview(
                    file.Id,
                    fileName,
                    health.Health,
                    health.Reason,
                    null,
                    "This file's chapters are not corrupt, so there is nothing to repair."), null, null);
            }

            return (null, tags, health);
        }
    }
}
