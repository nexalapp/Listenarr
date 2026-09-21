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
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Conversion;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Chapters
{
    /// <summary>
    /// Where a plan comes from — the file's own track, the edition's list, what the
    /// narrator announced, the damaged atom — and how a plan, once worked out, is kept
    /// on the file so nothing is listened to twice.
    /// </summary>
    public sealed partial class ChapterRepairService
    {
        /// <summary>
        /// One planning attempt: the plan or the rejection, and — when a source that
        /// should have answered could not be reached — why the attempt does not count.
        /// </summary>
        private sealed record PlanAttempt(ChapterPlan? Plan, ChapterPlanRejection? Rejection, string? Unavailable = null)
        {
            public static PlanAttempt From((ChapterPlan? Plan, ChapterPlanRejection? Rejection) result, string? unavailable = null) =>
                new(result.Plan, result.Rejection, unavailable);
        }

        /// <summary>The corrupt-atom sources: the track the file still plays, then the edition, then the damaged atom.</summary>
        private async Task<PlanAttempt> PlanCorruptAsync(
            Audiobook audiobook,
            string fullPath,
            AudiobookFileTags tags,
            CancellationToken cancellationToken)
        {
            var (plan, _) = ChapterPlanner.Plan(tags.Chapters, tags.Duration, null, null);
            if (plan != null)
            {
                return new PlanAttempt(plan, null);
            }

            var edition = !string.IsNullOrWhiteSpace(audiobook.Asin)
                ? await FetchAudnexusAsync(audiobook.Asin, cancellationToken)
                : default;
            var recovered = tags.Atoms is { HasNeroAtom: true, NeroAtomError: not null }
                ? TryRecover(fullPath)
                : null;

            var result = ChapterPlanner.Plan(tags.Chapters, tags.Duration, edition.Edition, recovered);
            // A plan from the damaged atom alone, or none, while the edition went unasked:
            // the edition might have done better, so this does not stand.
            var editionMissed = edition.Unavailable && result.Plan?.Source != ChapterSource.Played;
            return PlanAttempt.From(result, editionMissed ? "Audnexus could not be reached for the edition's chapters." : null);
        }

        /// <summary>Whisper writes an emphasised word in capitals — "DRIVE, an Expanse story" — which a chapter title should not keep.</summary>
        private static string? Unshout(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < words.Length; index++)
            {
                var letters = words[index].Where(char.IsLetter).ToArray();
                if (letters.Length >= 2 && letters.All(char.IsUpper))
                {
                    var lower = words[index].ToLowerInvariant();
                    words[index] = char.ToUpperInvariant(lower[0]) + lower[1..];
                }
            }

            return string.Join(' ', words);
        }

        /// <summary>What a stored plan for this file holds for; null when the file cannot be stat'ed.</summary>
        private string? PlanKey(string fullPath, Audiobook audiobook, ApplicationSettings settings)
        {
            try
            {
                return ChapterPlanKeys.For(
                    fileSystem.GetFileLength(fullPath),
                    fileSystem.GetLastWriteTimeUtc(fullPath),
                    audiobook.Asin,
                    ChapterPlanKeys.ModelFor(settings.TranscriptionEnabled, settings.TranscriptionModel));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static ChapterPlanOutcome? ReadStoredPlan(AudiobookFile file, string key)
        {
            if (file.ChapterPlanJson == null || !string.Equals(file.ChapterPlanKey, key, StringComparison.Ordinal))
            {
                return null;
            }

            return ChapterPlanStorage.Deserialize(file.ChapterPlanJson);
        }

        private async Task StorePlanAsync(int fileId, ChapterPlanOutcome outcome, string key, CancellationToken cancellationToken)
        {
            if (fileRepository == null)
            {
                return;
            }

            try
            {
                await fileRepository.SetChapterPlanAsync(
                    fileId,
                    ChapterPlanStorage.Serialize(outcome),
                    key,
                    outcome.Plan != null,
                    DateTime.UtcNow,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not store the chapter plan for file {FileId}", fileId);
            }
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
        private async Task<PlanAttempt> PlanFromAnnouncementsAsync(
            Audiobook audiobook,
            string fullPath,
            AudiobookFileTags tags,
            ChapterHealth health,
            PlanProgress progress,
            CancellationToken cancellationToken)
        {
            var marks = tags.Chapters ?? [];
            if (marks.Count == 0)
            {
                return new PlanAttempt(null, new ChapterPlanRejection("The file has no marks to listen at."));
            }

            if (marks.Count > MaxMarksToHear)
            {
                return new PlanAttempt(null, new ChapterPlanRejection($"{marks.Count} marks is more than this will listen to ({MaxMarksToHear})."));
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            var listening = settings.TranscriptionEnabled
                && transcriber != null
                && await transcriber.IsAvailableAsync(cancellationToken);

            // Transcription wanted but the model not ready is a moment, not an answer.
            if (settings.TranscriptionEnabled && transcriber != null && !listening)
            {
                return new PlanAttempt(null, null, "Transcription is on but the whisper model is not available yet; check the log.");
            }

            (IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime)? fetchedEdition = null;
            (IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime)? edition = null;
            if (!string.IsNullOrWhiteSpace(audiobook.Asin))
            {
                var fetched = await FetchAudnexusAsync(audiobook.Asin, cancellationToken);
                if (fetched.Unavailable)
                {
                    return new PlanAttempt(null, null, "Audnexus could not be reached for the edition's chapters.");
                }

                fetchedEdition = fetched.Edition;
                if (fetched.Edition is { } source
                    && Math.Abs((source.Runtime - tags.Duration).TotalSeconds) <= tags.Duration.TotalSeconds * ChapterPlanner.RuntimeTolerance)
                {
                    edition = source;
                }
            }

            var model = listening ? ChapterPlanKeys.ModelFor(true, settings.TranscriptionModel) : null;
            try
            {
                // Listening at the marks may be followed by listening after the pauses.
                var attempt = await ListenAndPlanAsync(
                    audiobook,
                    fullPath,
                    tags,
                    health,
                    marks,
                    model,
                    edition,
                    listening ? progress.Span(0, 0.5) : progress,
                    cancellationToken);
                if (!listening || !(attempt.Rejection != null || IsThin(attempt.Plan, health, fetchedEdition?.Chapters.Count)))
                {
                    return attempt;
                }

                // The marks did not give up the chapters, or gave up too few: the
                // narrator announced little or nothing there. The marks may simply be
                // in the wrong places — a rip's tracks need not fall on the chapters —
                // so the audio itself is asked, with the marks among the candidates.
                var discovered = await DiscoverAsync(audiobook, fullPath, tags, marks, fetchedEdition, model, progress.Span(0.5, 1), cancellationToken);
                if (discovered.Unavailable != null)
                {
                    return discovered;
                }

                if (discovered.Plan != null && (attempt.Plan == null || discovered.Plan.Chapters.Count > attempt.Plan.Chapters.Count))
                {
                    return discovered;
                }

                if (attempt.Plan != null)
                {
                    return attempt;
                }

                return new PlanAttempt(null, new ChapterPlanRejection(
                    $"{attempt.Rejection!.Reason.TrimEnd()} Listening after the pauses in the audio instead: {Uncapitalise(discovered.Rejection?.Reason)}"));
            }
            catch (TranscriptionFailedException ex)
            {
                return new PlanAttempt(null, null, ex.Message);
            }
        }

        /// <summary>
        /// A rip merged down to a chapter or two, or to well under what the edition
        /// lists, was announced at too few of its tracks to be believed: the chapters
        /// are more likely between the tracks than among them.
        /// </summary>
        private static bool IsThin(ChapterPlan? plan, ChapterHealth health, int? editionCount)
        {
            if (plan == null || health != ChapterHealth.Oversegmented || plan.Source != ChapterSource.Announcements)
            {
                return false;
            }

            return plan.Chapters.Count <= 3
                || (editionCount is { } expected && expected > 0 && plan.Chapters.Count * 5 < expected * 3);
        }

        private static string Uncapitalise(string? sentence) =>
            string.IsNullOrEmpty(sentence) ? "nothing was found." : char.ToLowerInvariant(sentence[0]) + sentence[1..];

        private async Task<PlanAttempt> ListenAndPlanAsync(
            Audiobook audiobook,
            string fullPath,
            AudiobookFileTags tags,
            ChapterHealth health,
            IReadOnlyList<EmbeddedChapter> marks,
            string? model,
            (IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime)? edition,
            PlanProgress progress,
            CancellationToken cancellationToken)
        {
            // The model that would listen; none means transcription is off.
            var listening = model != null;
            if (edition is { } matched)
            {
                // Listen only where the edition puts a chapter, for its name.
                IReadOnlyList<string?>? heardAtMarks = null;
                if (listening)
                {
                    // The edition's mark, and the rip's next one when it is a breath
                    // later: the announcement may sit on either.
                    var wanted = new HashSet<int>();
                    foreach (var chapter in matched.Chapters)
                    {
                        var nearest = ChapterRebuildPlanner.NearestMark(marks, chapter.Start);
                        if (nearest >= 0 && (marks[nearest].Start - chapter.Start).Duration() <= ChapterRebuildPlanner.SnapTolerance)
                        {
                            wanted.Add(nearest);
                            if (nearest + 1 < marks.Count && marks[nearest + 1].Start - marks[nearest].Start <= ChapterRebuildPlanner.LeadInTolerance)
                            {
                                wanted.Add(nearest + 1);
                            }
                        }
                    }

                    var partial = new string?[marks.Count];
                    var heardSoFar = 0;
                    foreach (var index in wanted)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        partial[index] = await HearAsync(fullPath, marks[index].Start, model, cancellationToken);
                        progress.Heard(++heardSoFar, wanted.Count);
                    }

                    heardAtMarks = partial;
                }

                var snapped = ChapterRebuildPlanner.SnapToMarks(matched.Chapters, marks, heardAtMarks, tags.Duration);
                if (snapped != null)
                {
                    return new PlanAttempt(snapped, null);
                }
            }

            if (!listening)
            {
                return new PlanAttempt(null, new ChapterPlanRejection(
                    health == ChapterHealth.Oversegmented
                        ? "No matching edition was found for this file, so finding the author's chapters among its tracks means listening for the announcements. Turn on transcription in Settings → Metadata Tags."
                        : "Naming placeholder chapters means listening for the announcements. Turn on transcription in Settings → Metadata Tags."));
            }

            var heard = new List<string?>(marks.Count);
            foreach (var mark in marks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                heard.Add(await HearAsync(fullPath, mark.Start, model, cancellationToken));
                progress.Heard(heard.Count, marks.Count);
            }

            // Placeholder titles on marks that are mostly unannounced are a CD rip the
            // threshold did not catch: the author's chapters begin at the announced
            // few, and the rest are tracks. Merge those; retitle when the marks are
            // themselves the chapters.
            var announced = heard.Count(text => ChapterAnnouncementParser.Parse(text) != null);
            if (announced == 0)
            {
                // A short work: credits, the story in parts, credits. The story's own
                // title comes from whichever credits named it.
                var storyTitle = Unshout(
                    AudioCreditsParser.Parse(heard[0]).Title
                    ?? AudioCreditsParser.Parse(heard[^1]).Title
                    ?? audiobook.Title);
                var (shaped, shapeRejection) = ChapterRebuildPlanner.NameFromCredits(
                    marks,
                    heard,
                    tags.Duration,
                    storyTitle,
                    AudioCreditsParser.LooksLikeOpeningCredits,
                    AudioCreditsParser.LooksLikeClosingCredits);
                if (shaped != null)
                {
                    return new PlanAttempt(shaped, null);
                }

                return health == ChapterHealth.GenericTitles
                    ? PlanAttempt.From(ChapterRebuildPlanner.Retitle(marks, heard, tags.Duration))
                    : new PlanAttempt(null, new ChapterPlanRejection(
                        $"No chapter announcements were heard at any mark, and {shapeRejection?.Reason.TrimEnd('.').ToLowerInvariant()}."));
            }

            if (health == ChapterHealth.GenericTitles
                && (announced < ChapterRebuildPlanner.MinimumAnnouncements || announced * 2 >= marks.Count))
            {
                return PlanAttempt.From(ChapterRebuildPlanner.Retitle(marks, heard, tags.Duration));
            }

            return PlanAttempt.From(ChapterRebuildPlanner.Merge(marks, heard, tags.Duration, edition?.Chapters.Count));
        }

        private async Task<string?> HearAsync(string fullPath, TimeSpan start, string? model, CancellationToken cancellationToken)
        {
            long length = 0;
            var lastWrite = DateTime.MinValue;
            try
            {
                length = fileSystem.GetFileLength(fullPath);
                lastWrite = fileSystem.GetLastWriteTimeUtc(fullPath);
                var cached = transcripts?.TryGet(fullPath, length, lastWrite, start, ListenWindow, model);
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
                    transcripts?.Set(fullPath, length, lastWrite, start, ListenWindow, transcript, model);
                }

                return transcript.Text;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                // Silence is an answer the planner acts on — merge this mark, call that
                // one credits — so a failure to listen must not pass for it.
                logger.LogWarning(ex, "Could not transcribe {Path} at {Start}", LogRedaction.SanitizeFilePath(fullPath), start);
                throw new TranscriptionFailedException($"Could not transcribe at {start:hh\\:mm\\:ss}: {ex.Message}", ex);
            }
        }

        /// <summary>A mark that could not be listened at, which no plan may be built over.</summary>
        private sealed class TranscriptionFailedException(string message, Exception inner) : Exception(message, inner);

        /// <summary>
        /// Progress across one file's planning, told in marks heard. Whisper gives no rate
        /// to estimate from, but the count of marks is known before the first is heard.
        /// A stage that may be followed by another takes a span of the file's share, so
        /// the bar never runs backwards when the second begins.
        /// </summary>
        private sealed class PlanProgress(IProgress<double>? progress, int fileIndex, int fileCount, double from = 0, double to = 1)
        {
            public void Heard(int done, int total)
            {
                if (progress == null || fileCount == 0 || total == 0)
                {
                    return;
                }

                progress.Report((fileIndex + from + (to - from) * done / total) / fileCount);
            }

            /// <summary>This file's progress between two fractions of it, for one stage of several.</summary>
            public PlanProgress Span(double start, double end) =>
                new(progress, fileIndex, fileCount, from + (to - from) * start, from + (to - from) * end);
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
    }
}
