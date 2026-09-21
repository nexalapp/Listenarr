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
using Listenarr.Domain.Audiobooks.Conversion;

namespace Listenarr.Domain.Audiobooks.Chapters
{
    /// <summary>
    /// Builds a chapter list for a file that has no marks, from what the narrator was
    /// heard to announce after its pauses, or from an edition's list laid over them.
    ///
    /// <para>
    /// The pauses are only candidates, and most are not chapters: a scene break, a
    /// breath before a long sentence, the gap after a jingle. So a candidate is a
    /// chapter only when an announcement was heard there, and the announcements must
    /// read as one book — climbing numbers, no chapter a few seconds long. An edition
    /// that names the same number of chapters lends its titles, which are the author's
    /// where whisper's are a guess at spelling.
    /// </para>
    /// </summary>
    public static class ChapterDiscoveryPlanner
    {
        /// <summary>A chapter shorter than this was two pauses around one announcement, not two chapters.</summary>
        public static readonly TimeSpan ShortestChapter = TimeSpan.FromSeconds(45);

        /// <summary>
        /// Fewer announcements than this among hundreds of pauses is prose that happened
        /// to mention a chapter, not a book announcing its own.
        /// </summary>
        public const int MinimumAnnouncements = 3;

        /// <summary>
        /// The chapters heard among the candidates.
        /// </summary>
        /// <param name="candidates">The marks listened at, in order, none at zero.</param>
        /// <param name="heard">What was heard at each, aligned with <paramref name="candidates"/>.</param>
        /// <param name="openingHeard">What was heard at the start of the file.</param>
        /// <param name="duration">The file's duration.</param>
        /// <param name="edition">The edition's chapters, when Audnexus has them, for their titles and count.</param>
        public static (ChapterPlan? Plan, ChapterPlanRejection? Rejection) FromAnnouncements(
            IReadOnlyList<TimeSpan> candidates,
            IReadOnlyList<string?> heard,
            string? openingHeard,
            TimeSpan duration,
            IReadOnlyList<EmbeddedChapter>? edition)
        {
            if (heard.Count != candidates.Count)
            {
                return (null, new ChapterPlanRejection("The pauses and what was heard after them do not line up."));
            }

            // One announcement between two pauses is heard at the first — the pause
            // before it — and again at the second when the window reaches it; and a
            // "Part Two" is followed a breath later by its "Chapter One". Either way
            // the chapter starts at the first pause, so the second is dropped, and
            // the chapter's own announcement, when it was the second, names the first.
            var announcements = heard.Select(ChapterAnnouncementParser.Parse).ToList();
            var texts = heard.ToList();
            for (var index = 1; index < announcements.Count; index++)
            {
                var current = announcements[index];
                var previous = announcements[index - 1];
                if (current == null || previous == null || candidates[index] - candidates[index - 1] >= ShortestChapter)
                {
                    continue;
                }

                if (current.Kind == "Chapter" && previous.Kind != "Chapter")
                {
                    texts[index - 1] = texts[index];
                    announcements[index - 1] = current;
                }

                announcements[index] = null;
            }

            var announced = announcements.Count(a => a != null);
            if (announced < MinimumAnnouncements)
            {
                return (null, new ChapterPlanRejection(
                    announced == 0
                        ? $"No chapter announcement was heard after any of the {candidates.Count} pause(s), so the chapters cannot be found. The narrator may not announce them."
                        : $"Only {announced} chapter announcement(s) were heard after {candidates.Count} pause(s), which is too few to trust as a book's chapters."));
            }

            var marks = new List<EmbeddedChapter> { new(null, TimeSpan.Zero, TimeSpan.Zero) };
            var said = new List<string?> { openingHeard };
            for (var index = 0; index < candidates.Count; index++)
            {
                marks.Add(new EmbeddedChapter(null, candidates[index], candidates[index]));
                said.Add(announcements[index] == null ? null : texts[index]);
            }

            var (plan, rejection) = ChapterRebuildPlanner.Merge(marks, said, duration, edition?.Count);
            if (plan == null)
            {
                return (null, rejection);
            }

            var chapters = plan.Chapters;
            if (edition is { Count: > 0 } && edition.Count == chapters.Count)
            {
                chapters = Retitle(chapters, edition);
            }

            var pauses = candidates.Count;
            var note = $"{chapters.Count} chapter(s) heard announced after {pauses} pause(s)";
            note += edition is { Count: > 0 } list
                ? (list.Count == chapters.Count ? "; matches Audnexus, whose titles are used." : $"; Audnexus lists {list.Count}.")
                : ".";
            var partial = edition is { Count: > 0 } && chapters.Count < edition.Count;
            return (plan with { Chapters = chapters, Partial = partial, Note = note }, null);
        }

        /// <summary>
        /// An edition laid over the file's pauses, with what was heard at each of its
        /// marks: the edition's titles, the narrator's where the edition's is a
        /// placeholder, and a note on how many marks the narrator confirmed.
        /// </summary>
        /// <param name="aligned">The edition in the file's time.</param>
        /// <param name="heard">What was heard at each aligned chapter's mark, or null when nothing was listened to.</param>
        /// <param name="duration">The file's duration.</param>
        /// <param name="fileStem">The filename without its extension, which some editions use as every title.</param>
        public static ChapterPlan FromEdition(AlignedEdition aligned, IReadOnlyList<string?>? heard, TimeSpan duration, string? fileStem)
        {
            var chapters = new List<EmbeddedChapter>(aligned.Chapters.Count);
            var confirmed = heard == null ? 0 : Weigh(heard).Confirmed;
            for (var index = 0; index < aligned.Chapters.Count; index++)
            {
                var chapter = aligned.Chapters[index];
                var announced = ChapterAnnouncementParser.Parse(heard != null && index < heard.Count ? heard[index] : null);
                var title = ChapterHealthAnalyzer.IsPlaceholderTitle(chapter.Title, fileStem) && announced != null
                    ? announced.Title
                    : chapter.Title;
                chapters.Add(chapter with { Title = title });
            }

            var offset = aligned.Offset.Duration() < TimeSpan.FromSeconds(1)
                ? "no offset"
                : $"{(aligned.Offset > TimeSpan.Zero ? "+" : "-")}{aligned.Offset.Duration():m\\:ss} offset";
            var note = aligned.Landed > 0
                ? $"{chapters.Count} chapter(s) from Audnexus at {offset}, {aligned.Landed} of them on the file's own pauses"
                : $"{chapters.Count} chapter(s) from Audnexus, none of them on the file's own pauses; the runtime matches, so its marks are used as they are";
            note += heard == null ? "." : $"; {confirmed} confirmed by the narrator.";
            return new ChapterPlan(
                ChapterSource.Audnexus,
                WithEnds(chapters, duration),
                Partial: aligned.Landed == 0,
                note,
                heard?.ToList());
        }

        /// <summary>
        /// How the narrator bears on an aligned edition: marks where the announced number
        /// is the chapter's own, or an unnumbered heading, confirm it; a numbered
        /// announcement of some other chapter contradicts it, because the narrator is
        /// then announcing a different list at that mark.
        /// </summary>
        /// <param name="heard">What was heard at each aligned chapter's mark, in order.</param>
        public static (int Confirmed, int Contradicted) Weigh(IReadOnlyList<string?> heard)
        {
            var confirmed = 0;
            var contradicted = 0;
            for (var index = 0; index < heard.Count; index++)
            {
                var announced = ChapterAnnouncementParser.Parse(heard[index]);
                if (announced == null)
                {
                    continue;
                }

                if (announced.Number == null || announced.Kind != "Chapter" || announced.Number == index + 1)
                {
                    confirmed++;
                }
                else
                {
                    contradicted++;
                }
            }

            return (confirmed, contradicted);
        }

        /// <summary>The edition's titles on the heard marks, keeping a heard subtitle the edition lacks.</summary>
        private static List<EmbeddedChapter> Retitle(IReadOnlyList<EmbeddedChapter> chapters, IReadOnlyList<EmbeddedChapter> edition)
        {
            var titled = new List<EmbeddedChapter>(chapters.Count);
            for (var index = 0; index < chapters.Count; index++)
            {
                var editionTitle = edition[index].Title?.Trim();
                titled.Add(chapters[index] with { Title = string.IsNullOrWhiteSpace(editionTitle) ? chapters[index].Title : editionTitle });
            }

            return titled;
        }

        private static List<EmbeddedChapter> WithEnds(IReadOnlyList<EmbeddedChapter> chapters, TimeSpan duration)
        {
            var result = new List<EmbeddedChapter>(chapters.Count);
            for (var index = 0; index < chapters.Count; index++)
            {
                var start = index == 0 ? TimeSpan.Zero : chapters[index].Start;
                var end = index + 1 < chapters.Count
                    ? chapters[index + 1].Start
                    : (duration > TimeSpan.Zero ? duration : chapters[index].End);
                var title = string.IsNullOrWhiteSpace(chapters[index].Title) ? $"Chapter {index + 1}" : chapters[index].Title!.Trim();
                result.Add(new EmbeddedChapter(title, start, end));
            }

            return result;
        }
    }
}
