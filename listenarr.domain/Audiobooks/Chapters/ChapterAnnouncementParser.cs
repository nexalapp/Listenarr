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
using System.Globalization;
using System.Text.RegularExpressions;

namespace Listenarr.Domain.Audiobooks.Chapters
{
    /// <summary>What a narrator announced at the top of a stretch of audio, if anything.</summary>
    /// <param name="Kind">Chapter, Part, Book, or a named section such as Prologue.</param>
    /// <param name="Number">The number spoken, for the numbered kinds.</param>
    /// <param name="Title">The normalised title to write: "Chapter 4", "Chapter 4: Saint Nick", "Part 2", "Epilogue".</param>
    public sealed record ChapterAnnouncement(string Kind, int? Number, string Title);

    /// <summary>
    /// Finds a chapter announcement in the first words of a transcript.
    ///
    /// <para>
    /// Narrators announce chapters in a handful of shapes — "Chapter Four", "Chapter 4:
    /// The Long Night", "Part Two", "Prologue" — and some read only the heading the
    /// author wrote: "One. Saint Nick." All of them come at the very start of the
    /// chapter, so the parser looks only at the opening words: an announcement further
    /// in is a character talking about a chapter, and "the end of chapter four" is the
    /// tail of the previous chapter, not the head of this one.
    /// </para>
    /// <para>
    /// The transcript's segments matter for the bare-number shape. "Two men walked in"
    /// starts with a number too; what tells a heading from prose is that the heading is
    /// its own breath — whisper ends the segment after it — and is closed with a stop.
    /// A short phrase in that same breath, or alone in the next one, is the chapter's
    /// name and is kept as a subtitle.
    /// </para>
    /// </summary>
    public static partial class ChapterAnnouncementParser
    {
        /// <summary>How far into the transcript an announcement may start, in words.</summary>
        public const int LeadWords = 8;

        /// <summary>A subtitle longer than this is the first sentence of the chapter, not its name.</summary>
        public const int MaxSubtitleWords = 5;

        private static readonly Dictionary<string, int> Units = new(StringComparer.OrdinalIgnoreCase)
        {
            ["zero"] = 0,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
            ["six"] = 6,
            ["seven"] = 7,
            ["eight"] = 8,
            ["nine"] = 9,
            ["ten"] = 10,
            ["eleven"] = 11,
            ["twelve"] = 12,
            ["thirteen"] = 13,
            ["fourteen"] = 14,
            ["fifteen"] = 15,
            ["sixteen"] = 16,
            ["seventeen"] = 17,
            ["eighteen"] = 18,
            ["nineteen"] = 19,
            ["first"] = 1,
            ["second"] = 2,
            ["third"] = 3,
            ["fourth"] = 4,
            ["fifth"] = 5,
            ["sixth"] = 6,
            ["seventh"] = 7,
            ["eighth"] = 8,
            ["ninth"] = 9,
            ["tenth"] = 10,
            ["eleventh"] = 11,
            ["twelfth"] = 12,
            ["thirteenth"] = 13,
            ["fourteenth"] = 14,
            ["fifteenth"] = 15,
            ["sixteenth"] = 16,
            ["seventeenth"] = 17,
            ["eighteenth"] = 18,
            ["nineteenth"] = 19
        };

        private static readonly Dictionary<string, int> Tens = new(StringComparer.OrdinalIgnoreCase)
        {
            ["twenty"] = 20,
            ["thirty"] = 30,
            ["forty"] = 40,
            ["fifty"] = 50,
            ["sixty"] = 60,
            ["seventy"] = 70,
            ["eighty"] = 80,
            ["ninety"] = 90,
            ["twentieth"] = 20,
            ["thirtieth"] = 30,
            ["fortieth"] = 40,
            ["fiftieth"] = 50,
            ["sixtieth"] = 60,
            ["seventieth"] = 70,
            ["eightieth"] = 80,
            ["ninetieth"] = 90
        };

        private static readonly string[] NamedSections =
        [
            "prologue", "epilogue", "introduction", "preface", "foreword", "afterword",
            "interlude", "intermission", "dedication", "epigraph", "acknowledgments",
            "acknowledgements", "author's note", "authors note", "a note from the author",
            "about the author", "the end", "credits", "appendix", "glossary", "postscript"
        ];

        [GeneratedRegex(@"[^a-z0-9' ]+", RegexOptions.IgnoreCase)]
        private static partial Regex Punctuation();

        [GeneratedRegex(@"\bend of (the )?(chapter|part|book)\b", RegexOptions.IgnoreCase)]
        private static partial Regex EndOf();

        [GeneratedRegex(@"^\s*\[[^\]]*\]\s*|^\s*\([^)]*\)\s*")]
        private static partial Regex LeadingBracket();

        /// <summary>A heading closed by a stop: "One." "Chapter 4:" "1 -".</summary>
        [GeneratedRegex(@"^\s*(?<head>[A-Za-z0-9' \-]+?)\s*(?:[.:–—]+|\s-\s)\s*(?<rest>.*)$")]
        private static partial Regex Heading();

        public static ChapterAnnouncement? Parse(string? transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return null;
            }

            var segments = transcript
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(segment => LeadingBracket().Replace(segment, string.Empty).Trim())
                .Where(segment => segment.Length > 0)
                .ToList();
            if (segments.Count == 0)
            {
                return null;
            }

            var leadText = string.Join(' ', segments.Take(2));
            var words = Words(leadText);
            if (words.Length == 0)
            {
                return null;
            }

            // "...the end of chapter three" opens a track only when the rip split a
            // chapter's closing words off; it is not this track's chapter.
            if (EndOf().IsMatch(string.Join(' ', words.Take(LeadWords + 4))))
            {
                return null;
            }

            return ParseKindWord(segments, words)
                ?? ParseBareNumber(segments)
                ?? ParseNamedSection(words);
        }

        /// <summary>"Chapter four", "Part 2: Departure", "Book three".</summary>
        private static ChapterAnnouncement? ParseKindWord(IReadOnlyList<string> segments, string[] words)
        {
            // "Book three, chapter one": the chapter is the mark, the book is context.
            ChapterAnnouncement? first = null;
            for (var index = 0; index < Math.Min(words.Length, LeadWords); index++)
            {
                if (!IsKind(words[index], out var kind))
                {
                    continue;
                }

                var (number, consumed) = ReadNumber(words, index + 1);
                if (number is not { } n || n <= 0)
                {
                    continue;
                }

                var subtitle = SubtitleAfter(segments, words, index + 1 + consumed);
                var announcement = new ChapterAnnouncement(kind, n, Title(kind, n, subtitle));
                if (kind == "Chapter")
                {
                    return announcement;
                }

                first ??= announcement;
            }

            return first;
        }

        /// <summary>"One. Saint Nick", "12: The Drop" — the author's heading, read as written.</summary>
        private static ChapterAnnouncement? ParseBareNumber(IReadOnlyList<string> segments)
        {
            var heading = Heading().Match(segments[0]);
            if (!heading.Success)
            {
                return null;
            }

            var headWords = Words(heading.Groups["head"].Value);
            if (headWords.Length == 0)
            {
                return null;
            }

            var (number, consumed) = ReadNumber(headWords, 0);
            if (number is not { } n || n <= 0 || consumed != headWords.Length)
            {
                return null;
            }

            // The rest of the segment, or the whole next one, is the chapter's name
            // when it is short enough to be a name.
            var rest = heading.Groups["rest"].Value.Trim();
            string? subtitle = null;
            if (rest.Length > 0)
            {
                subtitle = ShortEnough(rest);
                if (subtitle == null)
                {
                    // A number followed by a sentence is a sentence that starts with a number.
                    return null;
                }
            }
            else if (segments.Count > 1)
            {
                subtitle = ShortEnough(segments[1]);
            }

            return new ChapterAnnouncement("Chapter", n, Title("Chapter", n, subtitle));
        }

        private static ChapterAnnouncement? ParseNamedSection(string[] words)
        {
            for (var index = 0; index < Math.Min(words.Length, LeadWords); index++)
            {
                foreach (var section in NamedSections)
                {
                    var sectionWords = section.Split(' ');
                    if (index + sectionWords.Length <= words.Length
                        && sectionWords.Select((w, i) => string.Equals(words[index + i], w, StringComparison.OrdinalIgnoreCase)).All(match => match))
                    {
                        return new ChapterAnnouncement("Section", null, TitleCase(section));
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The chapter's name after its number, when there is one: the remainder of the
        /// heading's own segment, or the next segment on its own, if short enough.
        /// </summary>
        /// <param name="segments">The transcript's segments.</param>
        /// <param name="leadWords">The words of the first two segments.</param>
        /// <param name="afterWordIndex">The index in <paramref name="leadWords"/> of the first word after the number.</param>
        private static string? SubtitleAfter(IReadOnlyList<string> segments, string[] leadWords, int afterWordIndex)
        {
            var tokens = Token().Matches(segments[0]);
            if (afterWordIndex < tokens.Count)
            {
                // Still inside the first segment. A name follows a stop or a colon —
                // "Chapter 4: The Long Night" — where prose just runs on: "chapter 4 the
                // morning came".
                var numberEnd = tokens[afterWordIndex - 1].Index + tokens[afterWordIndex - 1].Length;
                var between = segments[0][numberEnd..tokens[afterWordIndex].Index];
                if (between.IndexOfAny([':', '.', '-', '–', '—']) < 0)
                {
                    return null;
                }

                return ShortEnough(segments[0][tokens[afterWordIndex].Index..]);
            }

            return afterWordIndex == tokens.Count && segments.Count > 1 && leadWords.Length > afterWordIndex
                ? ShortEnough(segments[1])
                : null;
        }

        [GeneratedRegex(@"[A-Za-z0-9']+")]
        private static partial Regex Token();

        /// <summary>A phrase short enough to be a name, with its closing stop removed; null otherwise.</summary>
        private static string? ShortEnough(string phrase)
        {
            // Whisper leaves a heading open — "Saint Nick", "The Devil's Questions" —
            // but closes a one-word one: "Stockings." A stop at the very end is allowed
            // on a heading of a word or two; on anything longer it is a sentence, and
            // a stop anywhere else, or a question or exclamation, is prose.
            var trimmed = phrase.Trim();
            if (trimmed.EndsWith('.') && Words(trimmed).Length <= 2)
            {
                trimmed = trimmed[..^1].TrimEnd();
            }

            if (trimmed.Length == 0 || trimmed.IndexOfAny(['.', '!', '?', ';']) >= 0)
            {
                return null;
            }

            trimmed = trimmed.TrimEnd(':', ',', '-', '–', '—').Trim();
            return trimmed.Length > 0 && Words(trimmed).Length <= MaxSubtitleWords ? trimmed : null;
        }

        private static string Title(string kind, int number, string? subtitle)
        {
            var head = $"{kind} {number.ToString(CultureInfo.InvariantCulture)}";
            return string.IsNullOrWhiteSpace(subtitle) ? head : $"{head}: {subtitle}";
        }

        private static string[] Words(string text) =>
            Punctuation().Replace(text, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static bool IsKind(string word, out string kind)
        {
            kind = word.ToLowerInvariant() switch
            {
                "chapter" or "chapters" => "Chapter",
                "part" => "Part",
                "book" => "Book",
                _ => string.Empty
            };
            return kind.Length > 0;
        }

        /// <summary>
        /// Read a number in digits or words from <paramref name="start"/>: "4", "four",
        /// "twenty four", "the fourth", "one hundred and three". Returns the value and
        /// how many words it spanned, or null when the words are not a number.
        /// </summary>
        internal static (int? Number, int Consumed) ReadNumber(IReadOnlyList<string> words, int start)
        {
            var index = start;
            // "chapter the fourth"
            if (index < words.Count && string.Equals(words[index], "the", StringComparison.OrdinalIgnoreCase))
            {
                index++;
            }

            if (index >= words.Count)
            {
                return (null, 0);
            }

            if (int.TryParse(words[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits))
            {
                return (digits, index - start + 1);
            }

            var total = 0;
            var consumed = 0;
            var seen = false;
            while (index < words.Count)
            {
                var word = words[index];
                if (Units.TryGetValue(word, out var unit))
                {
                    total += unit;
                    seen = true;
                    index++;
                    consumed = index - start;
                    // A unit ends the number unless "hundred" follows.
                    if (index < words.Count && string.Equals(words[index], "hundred", StringComparison.OrdinalIgnoreCase))
                    {
                        total *= 100;
                        index++;
                        consumed = index - start;
                        if (index < words.Count && string.Equals(words[index], "and", StringComparison.OrdinalIgnoreCase))
                        {
                            index++;
                        }

                        continue;
                    }

                    break;
                }

                if (Tens.TryGetValue(word, out var tens))
                {
                    total += tens;
                    seen = true;
                    index++;
                    consumed = index - start;
                    if (index < words.Count && Units.TryGetValue(words[index], out var following) && following is > 0 and < 10)
                    {
                        total += following;
                        index++;
                        consumed = index - start;
                    }

                    break;
                }

                if (string.Equals(word, "hundred", StringComparison.OrdinalIgnoreCase) && seen)
                {
                    total *= 100;
                    index++;
                    consumed = index - start;
                    continue;
                }

                break;
            }

            return seen ? (total, consumed) : (null, 0);
        }

        private static string TitleCase(string section) =>
            string.Join(' ', section.Split(' ').Select(word =>
                word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
