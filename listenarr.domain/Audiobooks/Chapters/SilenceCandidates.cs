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
namespace Listenarr.Domain.Audiobooks.Chapters
{
    /// <summary>A stretch of audio below the noise floor, as ffmpeg's silencedetect reports it.</summary>
    public readonly record struct SilenceSpan(TimeSpan Start, TimeSpan End)
    {
        public TimeSpan Length => End > Start ? End - Start : TimeSpan.Zero;
    }

    /// <summary>
    /// Picks, from every pause in a file, the ones worth listening after for a chapter
    /// announcement.
    ///
    /// <para>
    /// A file with no marks at all has no candidates of its own, so the pauses stand in
    /// for them: a chapter break is a longer silence than a sentence break in every
    /// production this library holds. The longer pauses are tried first; when they are
    /// too few for the book — fewer than the edition lists, or fewer than any book has —
    /// the shorter ones join them. Nothing here decides a chapter: what the narrator
    /// says after each pause does.
    /// </para>
    /// </summary>
    public static class SilenceCandidates
    {
        /// <summary>A pause this long is a chapter break more often than not.</summary>
        public static readonly TimeSpan PreferredSilence = TimeSpan.FromSeconds(2);

        /// <summary>The shortest pause worth listening after when the long ones are too few.</summary>
        public static readonly TimeSpan ShortestSilence = TimeSpan.FromSeconds(1);

        /// <summary>A pause this close to either end of the file is padding, not a break.</summary>
        public static readonly TimeSpan EdgeMargin = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Two pauses this close together are one break — an announcement, its own
        /// pause, then the prose. The longer pause is the break; the first on a tie.
        /// </summary>
        public static readonly TimeSpan Spacing = TimeSpan.FromSeconds(10);

        /// <summary>
        /// The mark is placed this far before speech resumes, so the first word of the
        /// chapter is not clipped by a player that seeks to the mark exactly.
        /// </summary>
        public static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(300);

        /// <summary>Fewer candidates than this from the long pauses alone, and the short ones join.</summary>
        public const int MinimumWanted = 4;

        /// <param name="silences">Every pause ffmpeg found, in any order.</param>
        /// <param name="duration">The file's duration.</param>
        /// <param name="wanted">How many chapters the book is thought to have, when known; the long pauses alone must reach it.</param>
        /// <param name="cap">The most candidates to return; the longest pauses win a place.</param>
        /// <param name="marks">
        /// Marks the file already carries, which join the pauses: a rip's track boundary
        /// is sample-accurate where a pause is a guess, so where the two are a breath
        /// apart the mark stands. Null or empty for a file with none.
        /// </param>
        /// <returns>The marks to listen at, in order, none at zero.</returns>
        public static IReadOnlyList<TimeSpan> Select(
            IReadOnlyList<SilenceSpan> silences,
            TimeSpan duration,
            int? wanted,
            int cap,
            IEnumerable<TimeSpan>? marks = null)
        {
            var inside = silences
                .Where(s => s.Length > TimeSpan.Zero && s.Start >= EdgeMargin && (duration <= TimeSpan.Zero || s.End <= duration - EdgeMargin))
                .OrderBy(s => s.Start)
                .ToList();

            var target = Math.Max(MinimumWanted, wanted ?? 0);
            var chosen = inside.Where(s => s.Length >= PreferredSilence).ToList();
            if (chosen.Count < target)
            {
                chosen = inside.Where(s => s.Length >= ShortestSilence).ToList();
            }

            var spaced = new List<SilenceSpan>(chosen.Count);
            foreach (var silence in chosen)
            {
                if (spaced.Count > 0 && silence.End - spaced[^1].End <= Spacing)
                {
                    if (silence.Length > spaced[^1].Length)
                    {
                        spaced[^1] = silence;
                    }

                    continue;
                }

                spaced.Add(silence);
            }

            if (spaced.Count > cap)
            {
                spaced = spaced
                    .OrderByDescending(s => s.Length)
                    .ThenBy(s => s.Start)
                    .Take(cap)
                    .OrderBy(s => s.Start)
                    .ToList();
            }

            var candidates = spaced
                .Select(s => s.End - Lead > s.Start ? s.End - Lead : s.Start)
                .ToList();

            foreach (var mark in (marks ?? []).Where(m => m > TimeSpan.Zero && (duration <= TimeSpan.Zero || m < duration - EdgeMargin)).OrderBy(m => m))
            {
                var near = candidates.FindIndex(c => (c - mark).Duration() <= Spacing);
                if (near >= 0)
                {
                    candidates[near] = mark;
                }
                else
                {
                    candidates.Add(mark);
                }
            }

            return candidates.Distinct().OrderBy(c => c).ToList();
        }
    }
}
