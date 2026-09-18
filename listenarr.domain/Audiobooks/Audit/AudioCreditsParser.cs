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

namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>
    /// Reads the spoken credits out of a transcript.
    ///
    /// <para>
    /// Audiobooks open and close with a formula: the title, "by" the author, "read by"
    /// or "narrated by" the narrator, usually in that order and usually in the first
    /// minute. The parser looks for the connectives and takes the short phrases around
    /// them. It is deliberately loose — whisper drops commas and mishears names — and
    /// the result is evidence for a score, never a value that gets written anywhere.
    /// </para>
    /// </summary>
    public static partial class AudioCreditsParser
    {
        /// <summary>A credit longer than this is a sentence that happened to contain "by".</summary>
        public const int MaxCreditWords = 7;

        [GeneratedRegex(@"\b(?:read|narrated|performed|voiced)\s+(?:for you\s+)?by\s+(?<narrator>[A-Z][\w'.-]*(?:\s+(?:and\s+|&\s+)?[A-Z][\w'.-]*){0,5})", RegexOptions.IgnoreCase)]
        private static partial Regex NarratedBy();

        [GeneratedRegex(@"(?<title>[^.;:!?\n]{2,80}?)\s*[,.:]?\s+(?:written\s+)?by\s+(?<author>[A-Z][\w'.-]*(?:\s+(?:and\s+|&\s+)?[A-Z][\w'.-]*){0,4})", RegexOptions.IgnoreCase)]
        private static partial Regex TitleBy();

        [GeneratedRegex(@"^\s*(?:this is|welcome to|you are listening to|(?:[\w.&']+\s+){0,4}presents|(?:the )?audiobook(?: edition)? of|an? (?:\w+\s+)?audio ?(?:book|books)? (?:production|edition|presentation) of)\s*", RegexOptions.IgnoreCase)]
        private static partial Regex Preamble();

        [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
        private static partial Regex SoundTag();

        public static AudioCredits Parse(string? transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return AudioCredits.Empty;
            }

            // Whisper marks non-speech in brackets — "[Music]", "(applause)" — and those
            // are not part of any title.
            var text = SoundTag().Replace(transcript.Replace('\n', ' ').Replace('\r', ' '), " ");
            string? narrator = null;
            var narrated = NarratedBy().Match(text);
            if (narrated.Success)
            {
                narrator = Clean(narrated.Groups["narrator"].Value);
                // Take the narrator out so "read by X" cannot also read as "by X" the author.
                text = text.Remove(narrated.Index, narrated.Length);
            }

            string? title = null;
            string? author = null;
            var titled = TitleBy().Match(text);
            if (titled.Success)
            {
                author = Clean(titled.Groups["author"].Value);
                var rawTitle = Preamble().Replace(titled.Groups["title"].Value.Trim(), string.Empty);
                // The title is the last clause before "by": drop anything before a stop.
                var lastStop = rawTitle.LastIndexOfAny(['.', '!', '?', ';']);
                if (lastStop >= 0)
                {
                    rawTitle = rawTitle[(lastStop + 1)..];
                }

                title = Clean(rawTitle);
            }

            return new AudioCredits(
                title is { Length: > 0 } t && Words(t) <= MaxCreditWords * 2 ? t : null,
                author is { Length: > 0 } a && Words(a) <= MaxCreditWords ? a : null,
                narrator is { Length: > 0 } n && Words(n) <= MaxCreditWords ? n : null);
        }

        private static string Clean(string value) =>
            value.Trim().Trim('.', ',', ':', ';', '"', '“', '”', '\'', '-', '–', '—').Trim();

        private static int Words(string value) =>
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
