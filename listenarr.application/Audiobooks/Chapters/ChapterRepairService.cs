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
    /// Plans a chapter repair per file from the three sources the planner knows, and
    /// hands the accepted plans to the tag queue as one job for the book.
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

            var scope = fileIds == null ? null : new HashSet<int>(fileIds);
            var files = (audiobook.Files ?? [])
                .Where(file => TaggableFile.IsTaggable(file.Path))
                .Where(file => scope == null || scope.Contains(file.Id))
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var previews = new List<ChapterRepairFilePreview>(files.Count);
            foreach (var file in files)
            {
                var fullPath = AudiobookFilePaths.ResolveFullPath(audiobook, file);
                var fileName = Path.GetFileName(fullPath ?? file.Path ?? string.Empty);

                if (fullPath == null || !fileSystem.FileExists(fullPath))
                {
                    previews.Add(new ChapterRepairFilePreview(file.Id, fileName, ChapterHealth.Unknown, null, null, "The file is not readable from here."));
                    continue;
                }

                AudiobookFileTags tags;
                try
                {
                    tags = await tagWriter.ReadAsync(fullPath, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    previews.Add(new ChapterRepairFilePreview(file.Id, fileName, ChapterHealth.Unknown, null, null, $"The file could not be read: {ex.Message}"));
                    continue;
                }

                var health = ChapterHealthAnalyzer.Analyze(
                    tags.Chapters,
                    tags.Atoms,
                    tags.Duration,
                    Path.GetFileNameWithoutExtension(fileName));

                if (!ChapterHealthSeverity.IsRepairableKind(health.Health))
                {
                    previews.Add(new ChapterRepairFilePreview(
                        file.Id,
                        fileName,
                        health.Health,
                        health.Reason,
                        null,
                        "This file's chapters are not corrupt, so there is nothing to repair."));
                    continue;
                }

                // A plan worked out earlier for this exact file, book and settings holds.
                var settings = await configurationService.GetApplicationSettingsAsync();
                var key = PlanKey(fullPath, audiobook, settings.TranscriptionEnabled);
                var outcome = key != null ? ReadStoredPlan(file, key) : null;
                if (outcome == null)
                {
                    outcome = health.Health is ChapterHealth.Oversegmented or ChapterHealth.GenericTitles
                        ? ToOutcome(await PlanFromAnnouncementsAsync(audiobook, fullPath, tags, health.Health, cancellationToken))
                        : ToOutcome(await PlanCorruptAsync(audiobook, fullPath, tags, cancellationToken));
                    if (key != null)
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
            foreach (var file in (audiobook.Files ?? [])
                         .Where(file => TaggableFile.IsTaggable(file.Path))
                         .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                var fullPath = AudiobookFilePaths.ResolveFullPath(audiobook, file);
                var fileName = Path.GetFileName(fullPath ?? file.Path ?? string.Empty);
                if (fullPath == null || !fileSystem.FileExists(fullPath))
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

        public async Task<int> PlanAsync(int audiobookId, IReadOnlyCollection<int>? fileIds = null, CancellationToken cancellationToken = default)
        {
            var preview = await PreviewAsync(audiobookId, fileIds, cancellationToken);
            return preview?.Files.Count(file => file.Repairable) ?? 0;
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
                var reason = preview.Files.FirstOrDefault()?.Rejection ?? "No file in scope has a repairable chapter atom.";
                return new TagEnqueueResult(TagEnqueueOutcome.NothingToTag, Reason: reason);
            }

            return await tagQueue.EnqueueChapterRepairAsync(audiobookId, plans, TagTrigger.Manual, cancellationToken);
        }
    }
}
