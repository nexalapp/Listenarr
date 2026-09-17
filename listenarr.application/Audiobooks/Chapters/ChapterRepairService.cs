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
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Conversion;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Chapters
{
    /// <summary>
    /// Plans a chapter repair per file from the three sources the planner knows, and
    /// hands the accepted plans to the tag queue as one job for the book.
    /// </summary>
    public sealed class ChapterRepairService(
        IAudiobookRepository audiobookRepository,
        IAudiobookTagWriter tagWriter,
        IChapterAtomRecovery atomRecovery,
        ITagQueueService tagQueue,
        IFileSystem fileSystem,
        ILogger<ChapterRepairService> logger,
        IAudnexusService? audnexus = null) : IChapterRepairService
    {
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

            // Audnexus is asked once per book, and only if a file turns out to need it.
            (IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime)? audnexusChapters = null;
            var audnexusAsked = false;

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

                if (health.Health != ChapterHealth.Corrupt)
                {
                    previews.Add(new ChapterRepairFilePreview(
                        file.Id,
                        fileName,
                        health.Health,
                        health.Reason,
                        null,
                        health.Health == ChapterHealth.Oversegmented
                            ? "Merging CD tracks into chapters needs the audio audit, which is not available yet."
                            : "This file's chapter atom is not corrupt, so there is nothing to repair."));
                    continue;
                }

                // The file's own chapter track answers for most of these; the other
                // sources are only fetched when it does not.
                var (plan, rejection) = ChapterPlanner.Plan(tags.Chapters, tags.Duration, null, null);
                if (plan == null)
                {
                    if (!audnexusAsked && !string.IsNullOrWhiteSpace(audiobook.Asin))
                    {
                        audnexusAsked = true;
                        audnexusChapters = await FetchAudnexusAsync(audiobook.Asin, cancellationToken);
                    }

                    var recovered = tags.Atoms is { HasNeroAtom: true, NeroAtomError: not null }
                        ? TryRecover(fullPath)
                        : null;

                    (plan, rejection) = ChapterPlanner.Plan(tags.Chapters, tags.Duration, audnexusChapters, recovered);
                }
                previews.Add(new ChapterRepairFilePreview(
                    file.Id,
                    fileName,
                    health.Health,
                    health.Reason,
                    plan,
                    rejection?.Reason));
            }

            return new ChapterRepairPreview(audiobookId, previews);
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

        private IReadOnlyList<EmbeddedChapter>? TryRecover(string fullPath)
        {
            try
            {
                return atomRecovery.TryRecover(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
            {
                logger.LogDebug(ex, "Could not read the damaged chapter atom of {Path}", LogRedaction.SanitizeFilePath(fullPath));
                return null;
            }
        }

        private async Task<(IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime)?> FetchAudnexusAsync(
            string asin,
            CancellationToken cancellationToken)
        {
            if (audnexus == null)
            {
                return null;
            }

            try
            {
                var response = await audnexus.GetChaptersAsync(asin);
                if (response?.Chapters is not { Count: > 0 } chapters || response.RuntimeLengthMs is not { } runtimeMs)
                {
                    return null;
                }

                var list = new List<EmbeddedChapter>(chapters.Count);
                foreach (var chapter in chapters)
                {
                    if (chapter.StartOffsetMs is not { } startMs)
                    {
                        continue;
                    }

                    var start = TimeSpan.FromMilliseconds(startMs);
                    var end = chapter.LengthMs is { } lengthMs ? start + TimeSpan.FromMilliseconds(lengthMs) : start;
                    list.Add(new EmbeddedChapter(chapter.Title, start, end));
                }

                return (list, TimeSpan.FromMilliseconds(runtimeMs));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Audnexus chapters unavailable for {Asin}", asin);
                return null;
            }
        }
    }
}
