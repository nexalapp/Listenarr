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

using System.Text.RegularExpressions;

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
    public static partial class OpeningIdent
    {
        /// <summary>
        /// A shop or publisher badge read before the book proper: "This is Audible.",
        /// "Audible presents", "Recorded Books presents". Deliberately a short list of
        /// shapes rather than a guess, because skipping the first utterance of a book that
        /// opens with its own title throws the title away — measured on 2001: A Space
        /// Odyssey, whose opening line *is* "2001 A Space Odyssey by Arthur C. Clarke".
        /// </summary>
        [GeneratedRegex(@"^[""'\s]*(this is (audible|audible studios)\b|an audible original\b|[\w'&.,\- ]{0,40}\bpresents?\b)",
            RegexOptions.IgnoreCase)]
        private static partial Regex ShopIdent();

        /// <summary>Whether a line is nothing but a shop badge.</summary>
        public static bool IsShopIdent(string? line) =>
            !string.IsNullOrWhiteSpace(line)
            && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8
            && ShopIdent().IsMatch(line.Trim());

        private static readonly string[] CreditWording =
        [
            "narrated by", "read by", "performed by", "written by", "unabridged",
            "copyright", "production of", "an audiobook"
        ];

        /// <summary>
        /// Whether an opening looks like one whisper swallowed: it begins with a shop badge
        /// and then says nothing about what the book is. That is the shape the skip exists
        /// for, and the only shape it should be applied to.
        /// </summary>
        public static bool LooksSwallowed(string? opening)
        {
            if (string.IsNullOrWhiteSpace(opening))
            {
                return false;
            }

            var lines = opening.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0 || !IsShopIdent(lines[0]))
            {
                return false;
            }

            return !CreditWording.Any(word => opening.Contains(word, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A pause beginning later than this is the narrator breathing, not the end of an ident.</summary>
        public static readonly TimeSpan LatestIdentEnds = TimeSpan.FromSeconds(6);

        /// <summary>Shorter than this is a breath within the ident rather than the gap after it.</summary>
        public static readonly TimeSpan ShortestGap = TimeSpan.FromSeconds(0.4);

        /// <summary>
        /// A pause beginning before this is the lead-in hush at the head of the file, not
        /// the gap after anything. Files commonly open with half a second of it, and taking
        /// that as the end of the ident skips nothing and leaves the ident in the window.
        /// </summary>
        public static readonly TimeSpan LeadIn = TimeSpan.FromSeconds(0.25);

        /// <summary>Never skip more than this, whatever the pauses say. Beyond it, story is being thrown away.</summary>
        public static readonly TimeSpan MostToSkip = TimeSpan.FromSeconds(8);

        /// <summary>How far into the file the opening window should begin.</summary>
        public static TimeSpan StartsAfter(IReadOnlyList<SilenceSpan>? pauses)
        {
            if (pauses is not { Count: > 0 })
            {
                return TimeSpan.Zero;
            }

            // The gap wanted is the one after the ident, so it must follow some speech.
            var first = pauses
                .Where(pause => pause.Start >= LeadIn
                    && pause.Start < LatestIdentEnds
                    && pause.End - pause.Start >= ShortestGap)
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
