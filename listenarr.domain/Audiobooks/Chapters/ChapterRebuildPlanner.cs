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
    /// Rebuilds a chapter list from what the narrator announced at each existing mark.
    ///
    /// <para>
    /// The existing marks are the candidates: a CD rip put one at every track, and the
    /// author's chapters begin at some subset of them. A mark whose first words announce
    /// a chapter is kept and titled from the announcement; every other mark is folded
    /// into the chapter before it. The first mark is always kept, because the book
    /// starts there whatever was said. Nothing is moved: the marks a rip placed are
    /// sample-accurate and a transcript's timing is not.
    /// </para>
    /// </summary>
    public static class ChapterRebuildPlanner
    {
        /// <summary>Fewer announcements than this is a narrator who does not announce, not a rebuild.</summary>
        public const int MinimumAnnouncements = 2;

        /// <summary>
        /// Merge a CD rip's tracks into the announced chapters.
        /// </summary>
        /// <param name="marks">The existing marks, in order.</param>
        /// <param name="heard">What was heard after each mark, aligned with <paramref name="marks"/>.</param>
        /// <param name="duration">The file's duration.</param>
        /// <param name="expectedCount">How many chapters the edition has according to Audnexus, when known.</param>
        public static (ChapterPlan? Plan, ChapterPlanRejection? Rejection) Merge(
            IReadOnlyList<EmbeddedChapter> marks,
            IReadOnlyList<string?> heard,
            TimeSpan duration,
            int? expectedCount)
        {
            if (marks.Count == 0 || heard.Count != marks.Count)
            {
                return (null, new ChapterPlanRejection("The marks and what was heard at them do not line up."));
            }

            var announcements = heard.Select(ChapterAnnouncementParser.Parse).ToList();
            var announced = announcements.Count(a => a != null);
            if (announced < MinimumAnnouncements)
            {
                return (null, new ChapterPlanRejection(
                    announced == 0
                        ? "No chapter announcements were heard at any mark, so the author's chapters cannot be found. The narrator may not announce them."
                        : "Only one chapter announcement was heard, which is not enough to tell chapters from tracks."));
            }

            if (!NumbersAscend(announcements, out var why))
            {
                return (null, new ChapterPlanRejection($"The announcements heard do not read as one book's chapters: {why}"));
            }

            var kept = new List<EmbeddedChapter>();
            var keptHeard = new List<string?>();
            for (var index = 0; index < marks.Count; index++)
            {
                var announcement = announcements[index];
                if (index > 0 && announcement == null)
                {
                    continue;
                }

                var title = announcement?.Title ?? OpeningTitle(announcements);
                kept.Add(new EmbeddedChapter(title, marks[index].Start, marks[index].End));
                keptHeard.Add(heard[index]);
            }

            var chapters = WithEnds(kept, duration);
            var note = $"{chapters.Count} chapter(s) heard announced across {marks.Count} track(s)";
            if (expectedCount is { } expected && expected > 0)
            {
                var difference = Math.Abs(expected - chapters.Count);
                note += difference == 0
                    ? "; matches Audnexus."
                    : $"; Audnexus lists {expected}.";
            }
            else
            {
                note += ".";
            }

            return (new ChapterPlan(ChapterSource.Announcements, chapters, Partial: false, note, keptHeard), null);
        }

        /// <summary>How far an edition's mark may sit from a rip's mark and still be the same place.</summary>
        public static readonly TimeSpan SnapTolerance = TimeSpan.FromSeconds(2);

        /// <summary>At least this share of the edition's marks must land on the rip's marks for the edition to be this file.</summary>
        public const double MinimumSnapShare = 0.5;

        /// <summary>How far after an edition's mark the rip may place the announcement and still mean the same chapter.</summary>
        public static readonly TimeSpan LeadInTolerance = TimeSpan.FromSeconds(90);

        /// <summary>The rip mark nearest a time, or -1 when there are none.</summary>
        public static int NearestMark(IReadOnlyList<EmbeddedChapter> marks, TimeSpan at)
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

        private static string? Said(IReadOnlyList<string?>? heard, int index) =>
            heard != null && index >= 0 && index < heard.Count ? heard[index] : null;

        /// <summary>An announcement of this chapter number, or an unnumbered one (a prologue is chapter one of a kind).</summary>
        private static bool Matches(ChapterAnnouncement? announcement, int number) =>
            announcement != null && (announcement.Number == number || announcement.Number == null);

        /// <summary>
        /// Lay an edition's chapter list (Audnexus) over a rip's marks. Where an edition
        /// mark lands within <see cref="SnapTolerance"/> of a rip mark, the rip's mark
        /// is used — it is sample-accurate where the edition's is rounded. Returns null
        /// when too few land, which means the rip is not this edition however well the
        /// runtime agrees.
        /// </summary>
        /// <param name="edition">The edition's chapters.</param>
        /// <param name="marks">The rip's marks.</param>
        /// <param name="heard">What was heard at each rip mark, aligned with <paramref name="marks"/>, or null when nothing was listened to.</param>
        /// <param name="duration">The file's duration.</param>
        public static ChapterPlan? SnapToMarks(
            IReadOnlyList<EmbeddedChapter> edition,
            IReadOnlyList<EmbeddedChapter> marks,
            IReadOnlyList<string?>? heard,
            TimeSpan duration)
        {
            if (edition.Count == 0 || marks.Count == 0)
            {
                return null;
            }

            var snapped = new List<EmbeddedChapter>(edition.Count + 1);
            var snappedHeard = new List<string?>(edition.Count + 1);
            var landed = 0;
            var named = 0;
            for (var number = 0; number < edition.Count; number++)
            {
                var chapter = edition[number];
                var nearest = NearestMark(marks, chapter.Start);
                var onMark = nearest >= 0 && (marks[nearest].Start - chapter.Start).Duration() <= SnapTolerance;
                if (onMark)
                {
                    landed++;
                }

                // The edition may start a chapter at the credits the rip split off in
                // front of it: Audible counts the opening announcement as part of
                // chapter one, the rip does not. When the narrator announces this very
                // chapter at the next mark, a breath later, the next mark is the chapter.
                var chosen = nearest;
                var said = Said(heard, nearest);
                var announced = ChapterAnnouncementParser.Parse(said);
                if (onMark && nearest + 1 < marks.Count && marks[nearest + 1].Start - marks[nearest].Start <= LeadInTolerance)
                {
                    var nextSaid = Said(heard, nearest + 1);
                    var nextAnnounced = ChapterAnnouncementParser.Parse(nextSaid);
                    var thisNumber = number + 1;
                    if (!Matches(announced, thisNumber) && Matches(nextAnnounced, thisNumber))
                    {
                        if (number == 0 && marks[nearest].Start < marks[nearest + 1].Start)
                        {
                            snapped.Add(new EmbeddedChapter("Introduction", marks[nearest].Start, marks[nearest + 1].Start));
                            snappedHeard.Add(said);
                        }

                        chosen = nearest + 1;
                        said = nextSaid;
                        announced = nextAnnounced;
                    }
                }

                if (announced != null)
                {
                    named++;
                }

                var start = onMark ? marks[chosen].Start : chapter.Start;
                snapped.Add(new EmbeddedChapter(announced?.Title ?? chapter.Title, start, chapter.End));
                snappedHeard.Add(said);
            }

            if (landed < Math.Max(1, (int)Math.Ceiling(edition.Count * MinimumSnapShare)))
            {
                return null;
            }

            var note = $"{edition.Count} chapter(s) from Audnexus, {landed} of them on the file's own marks";
            note += heard == null ? "." : $"; {named} named from what was heard.";
            return new ChapterPlan(ChapterSource.Audnexus, WithEnds(snapped, duration), Partial: false, note, heard == null ? null : snappedHeard);
        }

        /// <summary>
        /// Keep every mark and put the announced title on those that have one. For a
        /// list whose marks are right and whose titles are placeholders.
        /// </summary>
        public static (ChapterPlan? Plan, ChapterPlanRejection? Rejection) Retitle(
            IReadOnlyList<EmbeddedChapter> marks,
            IReadOnlyList<string?> heard,
            TimeSpan duration)
        {
            if (marks.Count == 0 || heard.Count != marks.Count)
            {
                return (null, new ChapterPlanRejection("The marks and what was heard at them do not line up."));
            }

            var announcements = heard.Select(ChapterAnnouncementParser.Parse).ToList();
            var announced = announcements.Count(a => a != null);
            if (announced == 0)
            {
                return (null, new ChapterPlanRejection("No chapter announcements were heard at any mark, so there is nothing to title them with."));
            }

            var titled = new List<EmbeddedChapter>(marks.Count);
            for (var index = 0; index < marks.Count; index++)
            {
                var title = announcements[index]?.Title
                    ?? (index == 0 ? OpeningTitle(announcements) : marks[index].Title);
                titled.Add(new EmbeddedChapter(title, marks[index].Start, marks[index].End));
            }

            return (new ChapterPlan(
                ChapterSource.Announcements,
                WithEnds(titled, duration),
                Partial: announced < marks.Count,
                announced == marks.Count
                    ? $"Every one of the {marks.Count} chapter(s) was heard announced."
                    : $"{announced} of {marks.Count} chapter(s) were heard announced; the rest keep their titles.",
                heard.ToList()), null);
        }

        /// <summary>A credits chapter is short; longer than this and it is story.</summary>
        public static readonly TimeSpan CreditsMaximum = TimeSpan.FromMinutes(3);

        /// <summary>
        /// Shorter than this at either end is credits whatever was heard: no story has a
        /// half-minute first chapter, and whisper hears "This is Audible" over a jingle
        /// as often as not.
        /// </summary>
        public static readonly TimeSpan CreditsCertain = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Name the marks of a short work whose narrator announces nothing: a publisher's
        /// credits at the top, the story in one or more parts, credits at the end.
        ///
        /// <para>
        /// Novellas and short stories arrive this way — "Macmillan Audio presents
        /// Unauthorized Bread by Cory Doctorow" for nine seconds, three hour-long parts
        /// with no heading read, and "we hope you've enjoyed" for twenty — and the marks
        /// are exactly right; only the names are a tool's. So nothing moves: the short
        /// opening is the intro (credits, an epigraph, a prologue's first breath — it is
        /// not story), the short close is the credits, one part takes the story's own
        /// title, several are numbered.
        /// </para>
        /// </summary>
        public static (ChapterPlan? Plan, ChapterPlanRejection? Rejection) NameFromCredits(
            IReadOnlyList<EmbeddedChapter> marks,
            IReadOnlyList<string?> heard,
            TimeSpan duration,
            string? storyTitle,
            Func<string?, bool> looksLikeOpeningCredits,
            Func<string?, bool> looksLikeClosingCredits)
        {
            if (marks.Count < 2 || heard.Count != marks.Count)
            {
                return (null, new ChapterPlanRejection("Too few marks to be credits and a story."));
            }

            if (heard.Any(text => ChapterAnnouncementParser.Parse(text) != null))
            {
                return (null, new ChapterPlanRejection("Chapters are announced here; this is not the credits shape."));
            }

            var openingLength = Length(0, marks, duration);
            var closingLength = Length(marks.Count - 1, marks, duration);
            var opening = openingLength <= CreditsCertain
                || (openingLength <= CreditsMaximum && looksLikeOpeningCredits(heard[0]));
            var closing = closingLength <= CreditsCertain
                || (closingLength <= CreditsMaximum && looksLikeClosingCredits(heard[^1]));
            if (!opening && !closing)
            {
                return (null, new ChapterPlanRejection("Neither the first nor the last mark sounds like credits, so the marks cannot be named without announcements."));
            }

            var first = opening ? 1 : 0;
            var last = closing ? marks.Count - 2 : marks.Count - 1;
            var parts = last - first + 1;
            if (parts < 1)
            {
                return (null, new ChapterPlanRejection("The file is credits with no story between them."));
            }

            var titled = new List<EmbeddedChapter>(marks.Count);
            for (var index = 0; index < marks.Count; index++)
            {
                string title;
                if (opening && index == 0)
                {
                    title = "Intro";
                }
                else if (closing && index == marks.Count - 1)
                {
                    title = "Credits";
                }
                else if (parts == 1)
                {
                    title = string.IsNullOrWhiteSpace(storyTitle) ? "Part 1" : storyTitle.Trim();
                }
                else
                {
                    title = $"Part {index - first + 1}";
                }

                titled.Add(new EmbeddedChapter(title, marks[index].Start, marks[index].End));
            }

            var shape = (opening, closing) switch
            {
                (true, true) => "an intro, the story, the credits",
                (true, false) => "an intro, then the story",
                _ => "the story, then the credits"
            };
            var note = parts == 1
                ? $"Named from the shape of a short work — {shape}."
                : $"Named from the shape of a short work — {shape} — in {parts} parts. The narrator announces no chapters, so the parts are numbered.";
            return (new ChapterPlan(ChapterSource.Credits, WithEnds(titled, duration), Partial: false, note, heard.ToList()), null);
        }

        private static TimeSpan Length(int index, IReadOnlyList<EmbeddedChapter> marks, TimeSpan duration)
        {
            var mark = marks[index];
            var end = index + 1 < marks.Count ? marks[index + 1].Start : (duration > TimeSpan.Zero ? duration : mark.End);
            return end - mark.Start;
        }

        /// <summary>
        /// The first mark's title when nothing was announced there: the space before
        /// Chapter 1 is an introduction when a Chapter 1 follows, and Chapter 1 itself
        /// when the numbering starts later or not at all.
        /// </summary>
        private static string OpeningTitle(IReadOnlyList<ChapterAnnouncement?> announcements)
        {
            var firstNumbered = announcements.FirstOrDefault(a => a?.Number != null);
            return firstNumbered?.Number == 1 ? "Introduction" : "Chapter 1";
        }

        /// <summary>Numbered announcements of one kind must climb; a book restarting at chapter one mid-way is two books or a mishearing.</summary>
        private static bool NumbersAscend(IReadOnlyList<ChapterAnnouncement?> announcements, out string reason)
        {
            reason = string.Empty;
            var last = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var announcement in announcements)
            {
                if (announcement?.Number is not { } number)
                {
                    continue;
                }

                if (last.TryGetValue(announcement.Kind, out var previous) && number <= previous)
                {
                    reason = $"{announcement.Kind} {number} was heard after {announcement.Kind} {previous}.";
                    return false;
                }

                last[announcement.Kind] = number;
            }

            return true;
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
                result.Add(new EmbeddedChapter(chapters[index].Title, start, end));
            }

            return result;
        }
    }
}
