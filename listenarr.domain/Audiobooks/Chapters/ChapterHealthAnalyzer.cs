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
using System.Text.RegularExpressions;
using Listenarr.Domain.Audiobooks.Conversion;

namespace Listenarr.Domain.Audiobooks.Chapters
{
    /// <summary>
    /// Decides <see cref="ChapterHealth"/> from what ffprobe read and what the atoms say.
    ///
    /// <para>
    /// The two sources are checked against each other because neither is trustworthy
    /// alone. ffprobe hides a broken <c>chpl</c> atom by falling back to the QuickTime
    /// chapter track, so a file can read as perfectly chaptered while the atom Plex and
    /// Prologue parse is garbage. The atom check alone cannot see over-segmentation or
    /// placeholder titles, which are only visible in the list ffprobe returns.
    /// </para>
    /// </summary>
    public static partial class ChapterHealthAnalyzer
    {
        /// <summary>At or above this many chapters, a short median length reads as CD tracks.</summary>
        public const int OversegmentedMinimumCount = 15;

        /// <summary>Below this median length, chapters are tracks rather than chapters.</summary>
        public static readonly TimeSpan OversegmentedMaximumMedian = TimeSpan.FromMinutes(5);

        /// <summary>A book longer than this with no marks at all is worth flagging.</summary>
        public static readonly TimeSpan UnchapteredMinimumDuration = TimeSpan.FromMinutes(30);

        /// <summary>
        /// The shapes a ripping tool leaves: a zero-padded "Chapter 001", a "Track 3",
        /// and either with the track's length stuck on the end. A plain "Chapter 4" is
        /// not on the list — it is what a narrator says and what a publisher writes, and
        /// it is what a retitle from the announcements produces.
        /// </summary>
        [GeneratedRegex(@"^\s*((chapter|track)\s*\d+\s*-\s*\d{1,2}:\d{2}:\d{2}|track\s*\d+|chapter\s*0\d+)\s*$", RegexOptions.IgnoreCase)]
        private static partial Regex PlaceholderTitle();

        /// <param name="chapters">What ffprobe read; null when the file was not probed for chapters.</param>
        /// <param name="atoms">What the bytes say; null for containers that have no atoms to inspect.</param>
        /// <param name="duration">The file's duration.</param>
        /// <param name="fileStem">The filename without its extension, which some rippers use as every title.</param>
        public static ChapterHealthReport Analyze(
            IReadOnlyList<EmbeddedChapter>? chapters,
            ChapterAtomState? atoms,
            TimeSpan duration,
            string? fileStem)
        {
            if (chapters == null)
            {
                return ChapterHealthReport.Unknown;
            }

            var count = chapters.Count;
            var median = MedianLength(chapters);

            if (atoms != null)
            {
                if (atoms.HasNeroAtom && atoms.NeroAtomError != null)
                {
                    return new ChapterHealthReport(
                        ChapterHealth.Corrupt,
                        $"The chapter atom does not parse: {atoms.NeroAtomError}",
                        count,
                        median);
                }

                if (atoms.NeroAtomValid && atoms.NeroChapterCount != count)
                {
                    return new ChapterHealthReport(
                        ChapterHealth.Corrupt,
                        $"The chapter atom declares {atoms.NeroChapterCount} chapter(s) but the file plays {count}.",
                        count,
                        median);
                }

                if (count == 0 && (atoms.HasNeroAtom || atoms.HasChapterTrack))
                {
                    return new ChapterHealthReport(
                        ChapterHealth.Corrupt,
                        "The file carries chapter structures that yield no chapters.",
                        count,
                        median);
                }
            }

            if (!Monotonic(chapters, duration, out var why))
            {
                return new ChapterHealthReport(ChapterHealth.Corrupt, why, count, median);
            }

            if (count == 0)
            {
                return duration >= UnchapteredMinimumDuration
                    ? new ChapterHealthReport(
                        ChapterHealth.None,
                        $"No chapter marks in {Describe(duration)} of audio.",
                        0,
                        TimeSpan.Zero)
                    : new ChapterHealthReport(ChapterHealth.Healthy, "Short file, no chapters needed.", 0, TimeSpan.Zero);
            }

            if (count >= OversegmentedMinimumCount && median < OversegmentedMaximumMedian)
            {
                return new ChapterHealthReport(
                    ChapterHealth.Oversegmented,
                    $"{count} chapters with a median length of {Describe(median)} look like CD tracks.",
                    count,
                    median);
            }

            if (AllPlaceholders(chapters, fileStem))
            {
                return new ChapterHealthReport(
                    ChapterHealth.GenericTitles,
                    "Every chapter title is a placeholder.",
                    count,
                    median);
            }

            return new ChapterHealthReport(ChapterHealth.Healthy, $"{count} chapter(s).", count, median);
        }

        public static bool IsPlaceholderTitle(string? title, string? fileStem)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (PlaceholderTitle().IsMatch(title))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(fileStem)
                && string.Equals(title.Trim(), fileStem.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool AllPlaceholders(IReadOnlyList<EmbeddedChapter> chapters, string? fileStem)
        {
            // A single chapter titled after the file or "Chapter 1" is the normal shape of
            // a short book, not a placeholder problem.
            if (chapters.Count < 2)
            {
                return false;
            }

            foreach (var chapter in chapters)
            {
                if (!IsPlaceholderTitle(chapter.Title, fileStem))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Monotonic(IReadOnlyList<EmbeddedChapter> chapters, TimeSpan duration, out string reason)
        {
            reason = string.Empty;
            var previous = TimeSpan.MinValue;
            foreach (var chapter in chapters)
            {
                if (chapter.Start < TimeSpan.Zero || chapter.Start < previous)
                {
                    reason = "Chapter marks are out of order.";
                    return false;
                }

                if (duration > TimeSpan.Zero && chapter.Start > duration + TimeSpan.FromSeconds(1))
                {
                    reason = $"A chapter starts at {Describe(chapter.Start)}, past the end of {Describe(duration)} of audio.";
                    return false;
                }

                previous = chapter.Start;
            }

            return true;
        }

        private static TimeSpan MedianLength(IReadOnlyList<EmbeddedChapter> chapters)
        {
            if (chapters.Count == 0)
            {
                return TimeSpan.Zero;
            }

            var lengths = new List<TimeSpan>(chapters.Count);
            foreach (var chapter in chapters)
            {
                lengths.Add(chapter.End > chapter.Start ? chapter.End - chapter.Start : TimeSpan.Zero);
            }

            lengths.Sort();
            var middle = lengths.Count / 2;
            return lengths.Count % 2 == 1
                ? lengths[middle]
                : TimeSpan.FromTicks((lengths[middle - 1].Ticks + lengths[middle].Ticks) / 2);
        }

        private static string Describe(TimeSpan span) =>
            span.TotalHours >= 1
                ? $"{(int)span.TotalHours}h {span.Minutes:D2}m"
                : $"{(int)span.TotalMinutes}m {span.Seconds:D2}s";
    }
}
