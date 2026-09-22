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
    /// Finding chapters in a file that has none: the pauses stand in for marks, the
    /// edition's list is laid over them, and the narrator is listened to after each.
    ///
    /// <para>
    /// Every source is tried and they check each other. An edition whose marks land
    /// on the file's pauses is the author's list at the file's own timing; the narrator
    /// then confirms it, and where the narrator instead announces chapters at other
    /// pauses, the announcements win, because the pauses and the announcements are
    /// this recording and the edition may be another. Nothing is written from the
    /// pauses alone: a pause is a candidate, and only a source that names it makes it
    /// a chapter.
    /// </para>
    /// </summary>
    public sealed partial class ChapterRepairService
    {
        /// <summary>At least this share of an aligned edition's marks must be announced for the edition to stand without listening further.</summary>
        public const double EditionConfirmationShare = 0.5;

        private async Task<PlanAttempt> PlanUnchapteredAsync(
            Audiobook audiobook,
            string fullPath,
            AudiobookFileTags tags,
            PlanProgress progress,
            CancellationToken cancellationToken)
        {
            var settings = await configurationService.GetApplicationSettingsAsync();
            var listening = settings.TranscriptionEnabled
                && transcriber != null
                && await transcriber.IsAvailableAsync(cancellationToken);
            if (settings.TranscriptionEnabled && transcriber != null && !listening)
            {
                return new PlanAttempt(null, null, "Transcription is on but the whisper model is not available yet; check the log.");
            }

            (IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime)? edition = null;
            if (!string.IsNullOrWhiteSpace(audiobook.Asin))
            {
                var fetched = await FetchAudnexusAsync(audiobook.Asin, cancellationToken);
                if (fetched.Unavailable)
                {
                    return new PlanAttempt(null, null, "Audnexus could not be reached for the edition's chapters.");
                }

                edition = fetched.Edition;
            }

            return await DiscoverAsync(
                audiobook,
                fullPath,
                tags,
                [],
                edition,
                listening ? ChapterPlanKeys.ModelFor(true, settings.TranscriptionModel) : null,
                progress,
                cancellationToken);
        }

        /// <summary>
        /// Find the chapters from the audio itself: the pauses, and any marks the file
        /// already has, are the candidates; the edition is laid over them; the narrator
        /// is listened to after each. For a file with no marks, and for one whose marks
        /// the narrator did not confirm.
        /// </summary>
        /// <param name="audiobook"></param>
        /// <param name="fullPath"></param>
        /// <param name="tags"></param>
        /// <param name="marks">The file's own marks, which join the pauses as candidates; empty for a file with none.</param>
        /// <param name="edition">The edition's chapters, whatever its runtime; null when there are none to fetch.</param>
        /// <param name="model">The whisper model to listen with, or null when transcription is off.</param>
        /// <param name="progress"></param>
        /// <param name="cancellationToken"></param>
        private async Task<PlanAttempt> DiscoverAsync(
            Audiobook audiobook,
            string fullPath,
            AudiobookFileTags tags,
            IReadOnlyList<EmbeddedChapter> marks,
            (IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime)? edition,
            string? model,
            PlanProgress progress,
            CancellationToken cancellationToken)
        {
            if (silenceDetector == null)
            {
                return new PlanAttempt(null, new ChapterPlanRejection("This build cannot find the pauses in a file, so its chapters cannot be found from the audio."));
            }

            var listening = model != null;
            var hasAsin = !string.IsNullOrWhiteSpace(audiobook.Asin);
            if (!listening && edition == null)
            {
                // Nothing could name a pause: no list to lay over them and no one to listen. Not worth the decode.
                return new PlanAttempt(null, new ChapterPlanRejection(
                    (hasAsin
                        ? "Audnexus has no chapter list for this edition, so finding its chapters means listening for the announcements. "
                        : "The book has no ASIN to fetch an edition's chapters by, so finding its chapters means listening for the announcements. ")
                    + "Turn on transcription in Settings → Metadata Tags."));
            }

            IReadOnlyList<SilenceSpan> pauses;
            try
            {
                pauses = await silenceDetector.DetectAsync(fullPath, SilenceCandidates.ShortestSilence, cancellationToken) ?? [];
            }
            catch (SilenceDetectionException ex)
            {
                return new PlanAttempt(null, new ChapterPlanRejection($"The pauses in the audio could not be found: {ex.Message}"));
            }

            var candidates = SilenceCandidates.Select(pauses, tags.Duration, edition?.Chapters.Count, MaxMarksToHear, marks.Select(m => m.Start));
            // At information level because this is the work that takes the time: when a
            // planning job stops moving, this line is the last thing it said, and it says
            // how much listening it had signed up for.
            logger.LogInformation(
                "Listening for chapters in {Path}: {Candidates} mark(s) to hear",
                LogRedaction.SanitizeFilePath(fullPath),
                candidates.Count);
            logger.LogDebug(
                "{Count} pause(s) and {Marks} mark(s) in {Path} give {Candidates} candidate mark(s)",
                pauses.Count,
                marks.Count,
                LogRedaction.SanitizeFilePath(fullPath),
                candidates.Count);
            if (candidates.Count == 0)
            {
                return new PlanAttempt(null, new ChapterPlanRejection(
                    "No pause long enough to be a chapter break was found in the audio, so there is nowhere a chapter could begin."));
            }

            var fileStem = Path.GetFileNameWithoutExtension(fullPath);
            var hearing = new HearingBudget(progress, candidates.Count + 1 + (edition?.Chapters.Count ?? 0));

            try
            {
                // The edition over the pauses, confirmed by the narrator where one can listen.
                var aligned = edition is { } source ? Lay(source, candidates, tags.Duration) : null;
                IReadOnlyList<string?>? heardAtEdition = null;
                if (aligned != null)
                {
                    if (!listening)
                    {
                        return new PlanAttempt(ChapterDiscoveryPlanner.FromEdition(aligned, null, tags.Duration, fileStem), null);
                    }

                    heardAtEdition = await HearEachAsync(fullPath, aligned.Chapters.Select(c => c.Start), model, hearing, cancellationToken);
                    var (confirmed, contradicted) = ChapterDiscoveryPlanner.Weigh(heardAtEdition);
                    if (contradicted == 0 && confirmed >= Math.Max(1, (int)Math.Ceiling((aligned.Chapters.Count - 1) * EditionConfirmationShare)))
                    {
                        return new PlanAttempt(ChapterDiscoveryPlanner.FromEdition(aligned, heardAtEdition, tags.Duration, fileStem), null);
                    }

                    // A narrator announcing other chapters at the edition's marks is
                    // reading a different list: the edition is not this recording.
                    if (contradicted > 0)
                    {
                        aligned = null;
                    }

                    logger.LogDebug(
                        "The narrator confirmed {Confirmed} and contradicted {Contradicted} of {Count} edition mark(s) in {Path}; listening after every pause instead",
                        confirmed,
                        contradicted,
                        aligned?.Chapters.Count ?? 0,
                        LogRedaction.SanitizeFilePath(fullPath));
                }

                if (!listening)
                {
                    return new PlanAttempt(null, new ChapterPlanRejection(
                        "The edition's chapter list does not line up with the pauses in this file, so finding its chapters means listening for the announcements. "
                        + "Turn on transcription in Settings → Metadata Tags."));
                }

                // The narrator after every pause.
                var opening = await HearAsync(fullPath, TimeSpan.Zero, model, cancellationToken);
                hearing.Heard();
                var heard = await HearEachAsync(fullPath, candidates, model, hearing, cancellationToken);
                var (plan, rejection) = ChapterDiscoveryPlanner.FromAnnouncements(candidates, heard, opening, tags.Duration, edition?.Chapters);
                if (plan != null)
                {
                    return new PlanAttempt(plan, null);
                }

                // The narrator announces nothing, but the edition's marks sat on the
                // pauses: that is still a list for this recording, offered as such.
                if (aligned is { Landed: > 0 })
                {
                    var fromEdition = ChapterDiscoveryPlanner.FromEdition(aligned, heardAtEdition, tags.Duration, fileStem);
                    return new PlanAttempt(fromEdition with { Partial = true }, null);
                }

                return new PlanAttempt(null, rejection);
            }
            catch (TranscriptionFailedException ex)
            {
                return new PlanAttempt(null, null, ex.Message);
            }
        }

        /// <summary>
        /// The edition over the pauses; when it will not lie on them but the runtime
        /// says it is this recording, the edition's own marks, unsnapped.
        /// </summary>
        private static AlignedEdition? Lay((IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime) edition, IReadOnlyList<TimeSpan> candidates, TimeSpan duration)
        {
            var aligned = EditionAlignment.Align(edition.Chapters, candidates, duration);
            if (aligned != null)
            {
                return aligned;
            }

            var difference = Math.Abs((edition.Runtime - duration).TotalSeconds);
            if (edition.Chapters.Count < 2 || difference > duration.TotalSeconds * ChapterPlanner.RuntimeTolerance || !ChapterPlanner.IsSound(edition.Chapters, duration, out _))
            {
                return null;
            }

            var inside = edition.Chapters.Where((c, i) => i == 0 || c.Start < duration - ChapterPlanner.EndTolerance).ToList();
            return new AlignedEdition(TimeSpan.Zero, inside, 0, inside.Select(_ => false).ToList());
        }

        private async Task<List<string?>> HearEachAsync(
            string fullPath,
            IEnumerable<TimeSpan> starts,
            string? model,
            HearingBudget hearing,
            CancellationToken cancellationToken)
        {
            var heard = new List<string?>();
            foreach (var start in starts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                heard.Add(await HearAsync(fullPath, start, model, cancellationToken));
                hearing.Heard();
            }

            return heard;
        }

        /// <summary>Progress across the marks this file may be listened at; the count is a ceiling, since the edition may settle it early.</summary>
        private sealed class HearingBudget(PlanProgress progress, int total)
        {
            private int _done;

            public void Heard() => progress.Heard(Math.Min(++_done, total), total);
        }
    }
}
