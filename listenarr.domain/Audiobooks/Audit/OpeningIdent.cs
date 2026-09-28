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
using Listenarr.Domain.Audiobooks.Chapters;

namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>
    /// Where to start listening, given that a shop's ident comes first.
    ///
    /// <para>
    /// Whisper transcribes "This is Audible.", puts it in quotation marks, and then treats
    /// the rest of that thirty-second chunk as a continuation of the same quoted utterance
    /// and emits nothing at all. The credits live in those swallowed seconds, so the book
    /// is judged on prose alone and reported as some other book.
    /// </para>
    /// <para>
    /// Measured on Star Force: Endless Crusade with the same model and settings, only the
    /// window moved. From zero, ninety seconds of audio yielded "This is audible." and then
    /// silence until the thirtieth second. From two and a half seconds it yielded the title,
    /// the author, the narrator, a chapter heading and the whole passage - strictly more,
    /// not merely different. Shortening the window does not help; a twelve-second window
    /// from zero fails exactly as badly. Only skipping the ident works.
    /// </para>
    /// <para>
    /// So: begin at the end of the first pause, when there is one early enough and long
    /// enough to be the gap after an ident. Everything else starts at zero.
    /// </para>
    /// </summary>
    public static class OpeningIdent
    {
        /// <summary>A pause beginning later than this is the narrator breathing, not the end of an ident.</summary>
        public static readonly TimeSpan LatestIdentEnds = TimeSpan.FromSeconds(6);

        /// <summary>Shorter than this is a breath within the ident rather than the gap after it.</summary>
        public static readonly TimeSpan ShortestGap = TimeSpan.FromSeconds(0.4);

        /// <summary>Never skip more than this, whatever the pauses say. Beyond it, story is being thrown away.</summary>
        public static readonly TimeSpan MostToSkip = TimeSpan.FromSeconds(8);

        /// <summary>How far into the file the opening window should begin.</summary>
        public static TimeSpan StartsAfter(IReadOnlyList<SilenceSpan>? pauses)
        {
            if (pauses is not { Count: > 0 })
            {
                return TimeSpan.Zero;
            }

            var first = pauses
                .Where(pause => pause.Start < LatestIdentEnds && pause.End - pause.Start >= ShortestGap)
                .OrderBy(pause => pause.Start)
                .FirstOrDefault();

            if (first.End <= TimeSpan.Zero || first.End > MostToSkip)
            {
                return TimeSpan.Zero;
            }

            return first.End;
        }
    }
}
