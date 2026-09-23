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

        // A name is capitalised words, initials allowed, joined by "and" or "&". Case
        // matters here — without it "by the way" reads as an author called "the way" —
        // so the keywords carry their own (?i) and the pattern as a whole does not.
        private const string Name = @"(?:[A-Z]\.|[A-Z][\w'\-]*)(?:(?:\s+(?:(?i:and)\s+|&\s+)?|(?<=\.))(?:[A-Z]\.|[A-Z][\w'\-]*)){0,6}";

        // "red|reed" only in the "... for you by" form: whisper routinely hears "read
        // for you by" as "Red for you by", and that phrase cannot be anything else. A
        // bare "Red by X" is left alone, because it is a title far more often than a slip.
        [GeneratedRegex(@"\b(?:(?i:read|narrated|performed|voiced)\s+(?i:for you\s+)?|(?i:red|reed)\s+(?i:for you)\s+)(?i:by)\s+(?<narrator>" + Name + ")")]
        private static partial Regex NarratedBy();

        /// <summary>
        /// Where a closing stops crediting this book and starts advertising others.
        ///
        /// <para>
        /// A Random House tape ends "Among the many other titles by Michael Crichton,
        /// available on audio from Random House, are Airframe, read by Blair Brown, The
        /// Lost World, read by Anthony Heald...". Every name after that belongs to a
        /// different book, and the parser used to take the first of them as this book's
        /// narrator.
        /// </para>
        /// </summary>
        [GeneratedRegex(@"(?i:\b(?:other|more) (?:titles|books|audiobooks|works)\b|\balso available\b|\bavailable (?:on audio |in audio |from )|\blook for\b|\bcoming soon\b|\bother recordings\b)")]
        private static partial Regex Trailer();

        [GeneratedRegex(@"(?<title>[^.;:!?\n]{2,80}?)\s*[,.:]?\s+(?i:written\s+)?(?i:by)\s+(?<author>" + Name + ")")]
        private static partial Regex TitleBy();

        /// <summary>Words that say credits are being read, which is where a title before "by" is a title.</summary>
        [GeneratedRegex(@"(?i:presents|production of|recording of|audiobook|audio book|this has been|that was|listening to|written by|narrated by|read by|performed by)")]
        private static partial Regex Cue();

        [GeneratedRegex(@"^\s*(?i:this is|this has been|that was|you are listening to|you have been listening to|you've been listening to|welcome to|(?:[\w.&']+\s+){0,4}presents|(?:the )?audiobook(?: edition)? of|(?:an?|the) (?:[\w.&']+\s+){0,3}audio ?(?:book|books)? (?:production|recording|edition|presentation) of|(?:an?|the) (?:[\w.&']+\s+){0,3}(?:production|recording|presentation|edition) of)\s*")]
        private static partial Regex Preamble();

        /// <summary>The closing formula without an author: "This has been a Hachette Audio production of Drive."</summary>
        [GeneratedRegex(@"(?i:this has been|that was|you have been listening to|you've been listening to)\s+(?<title>[^.;:!?\n]{2,100})")]
        private static partial Regex ClosingTitle();

        [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
        private static partial Regex SoundTag();

        [GeneratedRegex(@"(?i:\bpresents\b|this is audible|audible presents|read (?:for you )?by|narrated by|performed by|written by|an? (?:[\w.&']+\s+){0,3}audio ?(?:book )?(?:production|recording|presentation|edition)|unabridged)")]
        private static partial Regex OpeningCue();

        [GeneratedRegex(@"(?i:this has been|that was|we hope you(?:'ve| have) enjoyed|thank you for listening|you(?:'ve| have) been listening|the end\b|produced by|directed by|copyright|all rights reserved|production of|audio production)")]
        private static partial Regex ClosingCue();

        /// <summary>Whether a stretch reads as a publisher's opening: "X presents Title by Author, read by Narrator".</summary>
        public static bool LooksLikeOpeningCredits(string? transcript) =>
            !string.IsNullOrWhiteSpace(transcript) && OpeningCue().IsMatch(transcript);

        /// <summary>Whether a stretch reads as the closing: "this has been", "we hope you've enjoyed", the copyright line.</summary>
        public static bool LooksLikeClosingCredits(string? transcript) =>
            !string.IsNullOrWhiteSpace(transcript) && ClosingCue().IsMatch(transcript);

        /// <summary>
        /// The credits of one book, read out of its opening and closing.
        ///
        /// <para>
        /// The two halves are parsed apart and the opening wins every field it fills.
        /// An opening names this book; a closing frequently sells others, and where they
        /// disagree the opening is the one talking about the recording in hand.
        /// </para>
        /// </summary>
        public static AudioCredits Parse(string? transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return AudioCredits.Empty;
            }

            var marker = transcript.IndexOf(AudioAuditTranscript.ClosingMarker, StringComparison.Ordinal);
            if (marker < 0)
            {
                return ParseOne(transcript);
            }

            var opening = ParseOne(transcript[..marker]);
            var closing = ParseOne(transcript[(marker + AudioAuditTranscript.ClosingMarker.Length)..]);
            return new AudioCredits(
                opening.Title ?? closing.Title,
                opening.Author ?? closing.Author,
                opening.Narrator ?? closing.Narrator);
        }

        private static AudioCredits ParseOne(string? transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return AudioCredits.Empty;
            }

            // Everything from the first "other titles by ..." on is an advertisement for
            // other books, and the names in it are theirs.
            var trailer = Trailer().Match(transcript);
            if (trailer.Success)
            {
                transcript = transcript[..trailer.Index];
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    return AudioCredits.Empty;
                }
            }

            // Whisper marks non-speech in brackets — "[Music]", "(applause)" — and those
            // are not part of any title. They do mark a break, as does the seam between
            // the opening and the closing, so each becomes a stop rather than a space:
            // otherwise the last words of the story run into the first of the credits.
            // A line break is a full stop, not a space. Whisper breaks lines where the
            // reader pauses, so "Read by Garrick Hagon\nThey looked out..." is two
            // sentences; flattened to a space, the story's first capitalised word joined
            // the narrator's name and the record was credited to "Garrick Hagon They".
            var text = SoundTag().Replace(
                transcript.Replace("\r\n", " . ").Replace("\n", " . ").Replace("\r", " . "),
                " . ");
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
            var titled = BestTitleBy(text);
            if (titled != null)
            {
                author = Clean(titled.Groups["author"].Value);
                title = Clean(StripPreamble(LastClause(titled.Groups["title"].Value)));
            }
            else
            {
                var closing = ClosingTitle().Match(text);
                if (closing.Success)
                {
                    title = Clean(StripPreamble(closing.Groups["title"].Value));
                }
            }

            return new AudioCredits(
                title is { Length: > 0 } t && Words(t) <= MaxCreditWords * 2 ? t : null,
                author is { Length: > 0 } a && Words(a) <= MaxCreditWords ? a : null,
                narrator is { Length: > 0 } n && Words(n) <= MaxCreditWords ? n : null);
        }

        /// <summary>
        /// Of every "X by Y" in the text, the one most likely to be the credits: an
        /// author of two or more words, near a cue such as "presents" or "read by", and
        /// with a short title. Prose says "by" too — "by the way", "by the river" — and
        /// the first match is as likely to be that as the credits.
        /// </summary>
        private static Match? BestTitleBy(string text)
        {
            Match? best = null;
            var bestScore = int.MinValue;
            foreach (Match match in TitleBy().Matches(text))
            {
                var author = match.Groups["author"].Value;
                var title = LastClause(match.Groups["title"].Value);
                var authorWords = Words(author);
                if (authorWords == 0 || authorWords > MaxCreditWords)
                {
                    continue;
                }

                var score = 0;
                score += authorWords >= 2 ? 2 : -1;
                score += Words(title) <= MaxCreditWords * 2 ? 1 : -3;
                var contextStart = Math.Max(0, match.Index - 80);
                var context = text.Substring(contextStart, Math.Min(text.Length - contextStart, match.Length + 160));
                score += Cue().IsMatch(context) ? 3 : 0;
                if (score > bestScore)
                {
                    best = match;
                    bestScore = score;
                }
            }

            return best;
        }

        /// <summary>"This has been a Hachette Audio production of Drive" → "Drive". Preambles stack, so strip until none is left.</summary>
        private static string StripPreamble(string value)
        {
            var current = value.Trim();
            while (true)
            {
                var next = Preamble().Replace(current, string.Empty, 1).Trim();
                if (next.Length == current.Length || next.Length == 0)
                {
                    return next.Length == 0 ? current : next;
                }

                current = next;
            }
        }

        /// <summary>The last clause before "by": drop anything before a stop.</summary>
        private static string LastClause(string value)
        {
            var trimmed = value.Trim();
            var lastStop = trimmed.LastIndexOfAny(['.', '!', '?', ';']);
            return lastStop >= 0 ? trimmed[(lastStop + 1)..] : trimmed;
        }

        private static string Clean(string value) =>
            value.Trim().Trim('.', ',', ':', ';', '"', '“', '”', '\'', '-', '–', '—').Trim();

        private static int Words(string value) =>
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
