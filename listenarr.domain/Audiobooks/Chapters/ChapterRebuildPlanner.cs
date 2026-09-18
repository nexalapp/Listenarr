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

            var snapped = new List<EmbeddedChapter>(edition.Count);
            var snappedHeard = new List<string?>(edition.Count);
            var landed = 0;
            var named = 0;
            foreach (var chapter in edition)
            {
                var nearest = -1;
                var distance = TimeSpan.MaxValue;
                for (var index = 0; index < marks.Count; index++)
                {
                    var gap = (marks[index].Start - chapter.Start).Duration();
                    if (gap < distance)
                    {
                        distance = gap;
                        nearest = index;
                    }
                }

                var onMark = nearest >= 0 && distance <= SnapTolerance;
                if (onMark)
                {
                    landed++;
                }

                var start = onMark ? marks[nearest].Start : chapter.Start;
                var said = onMark && heard != null && nearest < heard.Count ? heard[nearest] : null;
                var announced = ChapterAnnouncementParser.Parse(said);
                if (announced != null)
                {
                    named++;
                }

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
