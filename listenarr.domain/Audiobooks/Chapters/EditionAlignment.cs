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
    /// <summary>An edition's list laid over a file: shifted by one offset and snapped to the file's pauses.</summary>
    /// <param name="Offset">How much later the file runs than the edition; negative when it runs earlier.</param>
    /// <param name="Chapters">The edition's chapters in the file's time, snapped to a pause where one is near.</param>
    /// <param name="Landed">How many of the edition's marks after the first sit on a pause.</param>
    /// <param name="Snapped">Whether each chapter in <paramref name="Chapters"/> sits on a pause.</param>
    public sealed record AlignedEdition(TimeSpan Offset, IReadOnlyList<EmbeddedChapter> Chapters, int Landed, IReadOnlyList<bool> Snapped);

    /// <summary>
    /// Lays an edition's chapter list (Audnexus) over a file that has no marks of its
    /// own, using the file's pauses as the marks.
    ///
    /// <para>
    /// The same recording reaches a library with a different lead-in — a retailer's
    /// jingle, a longer publisher's credit, a trimmed silence — so the edition's marks
    /// are a constant shift from the file's. That shift is found, not assumed: every
    /// pause paired with one of the first few edition marks proposes an offset, and the
    /// offset under which most of the edition's marks land on a pause wins. Too few
    /// landing means a different recording, however close the runtime, and the answer
    /// is none.
    /// </para>
    /// </summary>
    public static class EditionAlignment
    {
        /// <summary>An edition mark this close to a pause sits on it. Audnexus rounds; a narrator breathes.</summary>
        public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(4);

        /// <summary>Further than this and it is not a lead-in but a different cut.</summary>
        public static readonly TimeSpan MaximumOffset = TimeSpan.FromMinutes(10);

        /// <summary>At least this share of the edition's marks, after the first, must land for the edition to be this file.</summary>
        public const double MinimumShare = 0.6;

        /// <summary>How many of the edition's leading marks propose offsets. The shift shows in the first chapters as well as in any.</summary>
        public const int Anchors = 6;

        /// <summary>Offsets closer than this are the same offset, so rounding in the edition's marks does not split the vote.</summary>
        public static readonly TimeSpan OffsetGrain = TimeSpan.FromMilliseconds(500);

        /// <param name="edition">The edition's chapters, in order, in the edition's time.</param>
        /// <param name="pauses">The file's candidate marks, in order.</param>
        /// <param name="duration">The file's duration.</param>
        public static AlignedEdition? Align(IReadOnlyList<EmbeddedChapter> edition, IReadOnlyList<TimeSpan> pauses, TimeSpan duration)
        {
            if (edition.Count < 2 || pauses.Count == 0)
            {
                return null;
            }

            var sorted = pauses.OrderBy(p => p).ToList();
            var votes = new Dictionary<long, TimeSpan>();
            void Propose(TimeSpan offset)
            {
                if (offset.Duration() > MaximumOffset)
                {
                    return;
                }

                var key = (long)Math.Round(offset.Ticks / (double)OffsetGrain.Ticks);
                votes.TryAdd(key, offset);
            }

            Propose(TimeSpan.Zero);
            foreach (var anchor in edition.Take(Anchors))
            {
                foreach (var pause in sorted)
                {
                    Propose(pause - anchor.Start);
                }
            }

            AlignedEdition? best = null;
            foreach (var offset in votes.Values)
            {
                var candidate = Lay(edition, sorted, duration, offset);
                if (candidate == null)
                {
                    continue;
                }

                if (best == null
                    || candidate.Landed > best.Landed
                    || (candidate.Landed == best.Landed && candidate.Offset.Duration() < best.Offset.Duration()))
                {
                    best = candidate;
                }
            }

            if (best == null)
            {
                return null;
            }

            var needed = Math.Max(2, (int)Math.Ceiling((edition.Count - 1) * MinimumShare));
            return best.Landed >= needed ? best : null;
        }

        /// <summary>The edition under one offset: each mark snapped to the nearest pause within tolerance, or left shifted.</summary>
        private static AlignedEdition? Lay(IReadOnlyList<EmbeddedChapter> edition, List<TimeSpan> pauses, TimeSpan duration, TimeSpan offset)
        {
            var chapters = new List<EmbeddedChapter>(edition.Count);
            var snapped = new List<bool>(edition.Count);
            var landed = 0;
            string? lost = null;
            for (var index = 0; index < edition.Count; index++)
            {
                var shifted = edition[index].Start + offset;
                if (index == 0)
                {
                    chapters.Add(edition[index] with { Start = TimeSpan.Zero });
                    snapped.Add(false);
                    continue;
                }

                if (shifted <= TimeSpan.Zero)
                {
                    // The file starts after this chapter began, so the file opens
                    // inside it: it is the opening chapter's name, not a mark.
                    lost = edition[index].Title;
                    continue;
                }

                if (duration > TimeSpan.Zero && shifted >= duration - ChapterPlanner.EndTolerance)
                {
                    // The file ends before this chapter: the edition is longer than the file.
                    return null;
                }

                var nearest = Nearest(pauses, shifted);
                var onPause = nearest is { } pause && (pause - shifted).Duration() <= Tolerance;
                if (onPause)
                {
                    landed++;
                }

                chapters.Add(edition[index] with { Start = onPause ? nearest!.Value : shifted });
                snapped.Add(onPause);
            }

            if (lost != null)
            {
                chapters[0] = chapters[0] with { Title = lost };
            }

            return chapters.Count < 2 ? null : new AlignedEdition(offset, chapters, landed, snapped);
        }

        private static TimeSpan? Nearest(List<TimeSpan> sorted, TimeSpan at)
        {
            if (sorted.Count == 0)
            {
                return null;
            }

            var index = sorted.BinarySearch(at);
            if (index >= 0)
            {
                return sorted[index];
            }

            index = ~index;
            var after = index < sorted.Count ? sorted[index] : (TimeSpan?)null;
            var before = index > 0 ? sorted[index - 1] : (TimeSpan?)null;
            if (after == null)
            {
                return before;
            }

            if (before == null)
            {
                return after;
            }

            return (at - before.Value) <= (after.Value - at) ? before : after;
        }
    }
}
