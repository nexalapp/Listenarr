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
        IConfigurationService configurationService,
        ILogger<ChapterRepairService> logger,
        IAudnexusService? audnexus = null,
        ITranscriber? transcriber = null,
        TranscriptCache? transcripts = null) : IChapterRepairService
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

                if (health.Health is ChapterHealth.Oversegmented or ChapterHealth.GenericTitles)
                {
                    var (heardPlan, heardRejection) = await PlanFromAnnouncementsAsync(
                        audiobook,
                        fullPath,
                        tags,
                        health.Health,
                        cancellationToken);
                    previews.Add(new ChapterRepairFilePreview(file.Id, fileName, health.Health, health.Reason, heardPlan, heardRejection?.Reason));
                    continue;
                }

                if (health.Health != ChapterHealth.Corrupt)
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

        /// <summary>
        /// Rebuild a CD rip's chapters, or name placeholder ones.
        ///
        /// <para>
        /// The edition's own list is tried first: when Audnexus's runtime matches and its
        /// marks land on the rip's marks, it is the author's chapters at no cost, and
        /// listening then only adds names. Otherwise every mark is listened at and the
        /// announcements decide which marks are chapters.
        /// </para>
        /// </summary>
        private async Task<(ChapterPlan? Plan, ChapterPlanRejection? Rejection)> PlanFromAnnouncementsAsync(
            Audiobook audiobook,
            string fullPath,
            AudiobookFileTags tags,
            ChapterHealth health,
            CancellationToken cancellationToken)
        {
            var marks = tags.Chapters ?? [];
            if (marks.Count == 0)
            {
                return (null, new ChapterPlanRejection("The file has no marks to listen at."));
            }

            if (marks.Count > MaxMarksToHear)
            {
                return (null, new ChapterPlanRejection($"{marks.Count} marks is more than this will listen to ({MaxMarksToHear})."));
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            var listening = settings.TranscriptionEnabled
                && transcriber != null
                && await transcriber.IsAvailableAsync(cancellationToken);

            (IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime)? edition = null;
            if (!string.IsNullOrWhiteSpace(audiobook.Asin))
            {
                var fetched = await FetchAudnexusAsync(audiobook.Asin, cancellationToken);
                if (fetched is { } source
                    && Math.Abs((source.Runtime - tags.Duration).TotalSeconds) <= tags.Duration.TotalSeconds * ChapterPlanner.RuntimeTolerance)
                {
                    edition = source;
                }
            }

            if (edition is { } matched)
            {
                // Listen only where the edition puts a chapter, for its name.
                IReadOnlyList<string?>? heardAtMarks = null;
                if (listening)
                {
                    var wanted = new HashSet<int>();
                    foreach (var chapter in matched.Chapters)
                    {
                        var nearest = NearestMark(marks, chapter.Start);
                        if (nearest >= 0 && (marks[nearest].Start - chapter.Start).Duration() <= ChapterRebuildPlanner.SnapTolerance)
                        {
                            wanted.Add(nearest);
                        }
                    }

                    var partial = new string?[marks.Count];
                    foreach (var index in wanted)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        partial[index] = await HearAsync(fullPath, marks[index].Start, cancellationToken);
                    }

                    heardAtMarks = partial;
                }

                var snapped = ChapterRebuildPlanner.SnapToMarks(matched.Chapters, marks, heardAtMarks, tags.Duration);
                if (snapped != null)
                {
                    return (snapped, null);
                }
            }

            if (!listening)
            {
                return (null, new ChapterPlanRejection(
                    settings.TranscriptionEnabled && transcriber != null
                        ? "Transcription is on but the whisper model is not available yet; check the log and try again."
                        : health == ChapterHealth.Oversegmented
                            ? "No matching edition was found for this file, so finding the author's chapters among its tracks means listening for the announcements. Turn on transcription in Settings → Metadata Tags."
                            : "Naming placeholder chapters means listening for the announcements. Turn on transcription in Settings → Metadata Tags."));
            }

            var heard = new List<string?>(marks.Count);
            foreach (var mark in marks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                heard.Add(await HearAsync(fullPath, mark.Start, cancellationToken));
            }

            // Placeholder titles on marks that are mostly unannounced are a CD rip the
            // threshold did not catch: the author's chapters begin at the announced
            // few, and the rest are tracks. Merge those; retitle when the marks are
            // themselves the chapters.
            var announced = heard.Count(text => ChapterAnnouncementParser.Parse(text) != null);
            if (health == ChapterHealth.GenericTitles
                && (announced < ChapterRebuildPlanner.MinimumAnnouncements || announced * 2 >= marks.Count))
            {
                return ChapterRebuildPlanner.Retitle(marks, heard, tags.Duration);
            }

            return ChapterRebuildPlanner.Merge(marks, heard, tags.Duration, edition?.Chapters.Count);
        }

        private static int NearestMark(IReadOnlyList<EmbeddedChapter> marks, TimeSpan at)
        {
            var nearest = -1;
            var distance = TimeSpan.MaxValue;
            for (var index = 0; index < marks.Count; index++)
            {
                var gap = (marks[index].Start - at).Duration();
                if (gap < distance)
                {
                    distance = gap;
                    nearest = index;
                }
            }

            return nearest;
        }

        private async Task<string?> HearAsync(string fullPath, TimeSpan start, CancellationToken cancellationToken)
        {
            long length = 0;
            var lastWrite = DateTime.MinValue;
            try
            {
                length = fileSystem.GetFileLength(fullPath);
                lastWrite = fileSystem.GetLastWriteTimeUtc(fullPath);
                var cached = transcripts?.TryGet(fullPath, length, lastWrite, start, ListenWindow);
                if (cached != null)
                {
                    return cached.Text;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not stat {Path} for the transcript cache", LogRedaction.SanitizeFilePath(fullPath));
            }

            try
            {
                var transcript = await transcriber!.TranscribeAsync(fullPath, start, ListenWindow, cancellationToken);
                logger.LogDebug(
                    "Heard at {Start} of {Path}: {Text}",
                    start,
                    LogRedaction.SanitizeFilePath(fullPath),
                    transcript.Text);
                if (length > 0)
                {
                    transcripts?.Set(fullPath, length, lastWrite, start, ListenWindow, transcript);
                }

                return transcript.Text;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Could not transcribe {Path} at {Start}", LogRedaction.SanitizeFilePath(fullPath), start);
                return null;
            }
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
