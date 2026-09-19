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
    /// Chooses the chapter list a corrupt file should be rewritten with.
    ///
    /// <para>
    /// Every candidate is a list somebody else produced — ffprobe, Audnexus, the bytes
    /// inside the broken atom — so the planner's whole job is to say which one is
    /// trustworthy for this file and to refuse when none is. It never invents marks:
    /// the one thing it synthesises is an opening chapter at zero in front of a
    /// recovered atom, because a book whose first chapter starts at minute seven is
    /// worse than one whose first chapter is called "Chapter 1".
    /// </para>
    /// </summary>
    public static class ChapterPlanner
    {
        /// <summary>How far Audnexus's runtime may differ from the file before it is a different edition.</summary>
        public const double RuntimeTolerance = 0.01;

        /// <summary>A mark this close to the end is a rounding artefact, not a chapter.</summary>
        public static readonly TimeSpan EndTolerance = TimeSpan.FromSeconds(1);

        /// <param name="played">What ffprobe read from the file, if anything.</param>
        /// <param name="duration">The file's duration.</param>
        /// <param name="audnexus">Audnexus's chapters and the runtime they were measured against, if fetched.</param>
        /// <param name="recovered">Entries recovered from the shifted atom, if any.</param>
        public static (ChapterPlan? Plan, ChapterPlanRejection? Rejection) Plan(
            IReadOnlyList<EmbeddedChapter>? played,
            TimeSpan duration,
            (IReadOnlyList<EmbeddedChapter> Chapters, TimeSpan Runtime)? audnexus,
            IReadOnlyList<EmbeddedChapter>? recovered)
        {
            if (played is { Count: > 0 } && IsSound(played, duration, out _))
            {
                return (new ChapterPlan(
                    ChapterSource.Played,
                    Normalise(played, duration),
                    Partial: false,
                    $"{played.Count} chapter(s) from the file's own chapter track."), null);
            }

            if (audnexus is { Chapters.Count: > 0 } source)
            {
                var difference = Math.Abs((source.Runtime - duration).TotalSeconds);
                var allowed = duration.TotalSeconds * RuntimeTolerance;
                if (difference <= allowed && IsSound(source.Chapters, duration, out _))
                {
                    return (new ChapterPlan(
                        ChapterSource.Audnexus,
                        Normalise(source.Chapters, duration),
                        Partial: false,
                        $"{source.Chapters.Count} chapter(s) from Audnexus; runtime matches within {difference:F0}s."), null);
                }
            }

            if (recovered is { Count: > 0 } && IsSound(recovered, duration, out _))
            {
                var list = new List<EmbeddedChapter>(recovered.Count + 1);
                if (recovered[0].Start > EndTolerance)
                {
                    list.Add(new EmbeddedChapter("Chapter 1", TimeSpan.Zero, recovered[0].Start));
                }

                list.AddRange(recovered);
                return (new ChapterPlan(
                    ChapterSource.RecoveredAtom,
                    Normalise(list, duration),
                    Partial: true,
                    $"{recovered.Count} chapter(s) recovered from the damaged atom; the opening chapter(s) are lost and one has been put in their place."), null);
            }

            var reason = played is { Count: > 0 }
                ? "The file's own chapter marks are not usable, and no other source matches this edition."
                : "The file carries no readable chapter marks, and no other source matches this edition.";
            return (null, new ChapterPlanRejection(reason));
        }

        /// <summary>Monotonic, inside the audio, and not all at zero.</summary>
        public static bool IsSound(IReadOnlyList<EmbeddedChapter> chapters, TimeSpan duration, out string reason)
        {
            reason = string.Empty;
            var previous = TimeSpan.MinValue;
            foreach (var chapter in chapters)
            {
                if (chapter.Start < TimeSpan.Zero || chapter.Start < previous)
                {
                    reason = "marks are out of order";
                    return false;
                }

                if (duration > TimeSpan.Zero && chapter.Start > duration + TimeSpan.FromSeconds(1))
                {
                    reason = "a mark lies past the end of the audio";
                    return false;
                }

                previous = chapter.Start;
            }

            if (chapters.Count > 1 && chapters[^1].Start == TimeSpan.Zero)
            {
                reason = "every mark is at zero";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Ends follow the next start, the last end is the duration, and a mark within a
        /// breath of the end is dropped: ffmpeg refuses an empty chapter.
        /// </summary>
        private static List<EmbeddedChapter> Normalise(IReadOnlyList<EmbeddedChapter> chapters, TimeSpan duration)
        {
            var kept = new List<EmbeddedChapter>(chapters.Count);
            foreach (var chapter in chapters)
            {
                if (duration > TimeSpan.Zero && chapter.Start >= duration - EndTolerance && kept.Count > 0)
                {
                    continue;
                }

                kept.Add(chapter);
            }

            var result = new List<EmbeddedChapter>(kept.Count);
            for (var index = 0; index < kept.Count; index++)
            {
                var start = index == 0 ? TimeSpan.Zero : kept[index].Start;
                var end = index + 1 < kept.Count ? kept[index + 1].Start : (duration > TimeSpan.Zero ? duration : kept[index].End);
                var title = string.IsNullOrWhiteSpace(kept[index].Title) ? $"Chapter {index + 1}" : kept[index].Title!.Trim();
                result.Add(new EmbeddedChapter(title, start, end));
            }

            return result;
        }
    }
}
