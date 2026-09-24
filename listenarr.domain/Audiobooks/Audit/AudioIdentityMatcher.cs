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
    /// Decides whether a transcript of a book's opening (and closing) agrees with the
    /// record: title, authors, narrators.
    ///
    /// <para>
    /// Scores are token containment with a little slack for whisper: a word of the
    /// title or a name counts as heard when a transcript word equals it or is within one
    /// edit of it, so "Zeck" heard as "Zach" still counts and "Meeker" heard as "Meaker"
    /// does too. Stop words are ignored, and a name is scored on its best alias, so the
    /// spelling the library keeps and the one the narrator says both work.
    /// </para>
    /// <para>
    /// The verdict is conservative in one direction: a book is only called a mismatch
    /// when neither its title nor its author was heard. A narrator not heard is its own,
    /// milder verdict, because many productions never credit the narrator aloud.
    /// </para>
    /// </summary>
    public static partial class AudioIdentityMatcher
    {
        public const double HeardThreshold = 0.6;
        public const double PartlyHeardThreshold = 0.34;

        /// <summary>Fewer words than this and nothing can be said either way.</summary>
        public const int MinimumWords = 12;

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "of", "and", "in", "to", "on", "for", "at", "by", "with",
            "from", "or", "is", "it", "its", "as", "book", "novel", "audiobook", "series",
            "volume", "part", "one", "two", "three", "unabridged", "edition"
        };

        [GeneratedRegex(@"[^a-z0-9' ]+", RegexOptions.IgnoreCase)]
        private static partial Regex Punctuation();

        /// <param name="transcript">Everything heard, openings and closings joined.</param>
        /// <param name="title">The record's title.</param>
        /// <param name="authors">The record's authors.</param>
        /// <param name="narrators">The record's narrators; empty when unknown.</param>
        /// <param name="aliases">Name aliases, so either spelling of a person counts.</param>
        /// <param name="recordedMinutes">The runtime the record claims; null when it claims none.</param>
        /// <param name="measuredMinutes">How long the files actually run; null when they cannot be measured.</param>
        public static AudioAuditResult Judge(
            string? transcript,
            string? title,
            IReadOnlyList<string>? authors,
            IReadOnlyList<string>? narrators,
            IReadOnlyList<AuthorAlias>? aliases,
            double? recordedMinutes = null,
            double? measuredMinutes = null)
        {
            var heard = Tokens(transcript ?? string.Empty);
            var credits = AudioCreditsParser.Parse(transcript);

            if (heard.Length < MinimumWords)
            {
                return AudioCompleteness.MissingShare(recordedMinutes, measuredMinutes) is { } gap
                    ? new AudioAuditResult(
                        AudioAuditVerdict.Incomplete,
                        $"Too little speech was heard to tell what the book is, and {AudioCompleteness.Describe(gap, recordedMinutes!.Value, measuredMinutes!.Value)}.",
                        0, 0, null, credits)
                    : new AudioAuditResult(
                        AudioAuditVerdict.Inconclusive,
                        "Too little speech was heard to tell what the book is.",
                        0, 0, null, credits);
            }

            var titleScore = Containment(Tokens(title ?? string.Empty), heard);
            var authorScore = BestName(authors, aliases, heard);
            // A credited narrator is judged against the credit, so the author's first
            // name cannot stand in for the narrator's.
            double? narratorScore = narrators is { Count: > 0 }
                ? BestName(narrators, aliases, credits.Narrator != null ? Tokens(credits.Narrator) : heard)
                : null;

            var titleHeard = titleScore >= HeardThreshold;
            var authorHeard = authorScore >= HeardThreshold;
            var titlePartly = titleScore >= PartlyHeardThreshold;
            var authorPartly = authorScore >= PartlyHeardThreshold;

            if (titleHeard || authorHeard || (titlePartly && authorPartly))
            {
                // A credited narrator respelled by the transcriber is still that narrator:
                // "Michael Narrowmore" is Mikael Naramore, "PJ Oakland" is P. J. Ochlan.
                // Letters alone cannot tell those from a different reader, so the name is
                // compared by sound before the book is called mismatched.
                if (narratorScore is { } n
                    && n < PartlyHeardThreshold
                    && credits.Narrator != null
                    && !SpokenNameSimilarity.IsAnyOf(credits.Narrator, narrators))
                {
                    return new AudioAuditResult(
                        AudioAuditVerdict.NarratorMismatch,
                        $"The book is the one on record, but the narrator heard is \"{credits.Narrator}\", not {Join(narrators!)}.",
                        titleScore, authorScore, narratorScore, credits);
                }

                // The credits agree, so this is the book. Whether all of it is here is a
                // separate question the credits cannot answer, because the opening and the
                // closing of a truncated file read exactly as they should.
                if (AudioCompleteness.MissingShare(recordedMinutes, measuredMinutes) is { } missing)
                {
                    return new AudioAuditResult(
                        AudioAuditVerdict.Incomplete,
                        $"The book is the one on record, but {AudioCompleteness.Describe(missing, recordedMinutes!.Value, measuredMinutes!.Value)}.",
                        titleScore, authorScore, narratorScore, credits);
                }

                var what = (titleHeard, authorHeard) switch
                {
                    (true, true) => "The title and the author were heard.",
                    (true, false) => "The title was heard.",
                    (false, true) => "The author was heard.",
                    _ => "Most of the title and the author were heard."
                };
                return new AudioAuditResult(AudioAuditVerdict.Match, what, titleScore, authorScore, narratorScore, credits);
            }

            if (credits.Title != null || credits.Author != null)
            {
                var said = credits.Title != null && credits.Author != null
                    ? $"\"{credits.Title}\" by {credits.Author}"
                    : credits.Title != null ? $"\"{credits.Title}\"" : $"a book by {credits.Author}";
                return new AudioAuditResult(
                    AudioAuditVerdict.Mismatch,
                    $"The audio introduces itself as {said}, not as {Describe(title, authors)}.",
                    titleScore, authorScore, narratorScore, credits);
            }

            if (!titlePartly && !authorPartly)
            {
                return new AudioAuditResult(
                    AudioAuditVerdict.Mismatch,
                    $"Neither the title nor the author of {Describe(title, authors)} was heard in the opening.",
                    titleScore, authorScore, narratorScore, credits);
            }

            if (AudioCompleteness.MissingShare(recordedMinutes, measuredMinutes) is { } tooShort)
            {
                return new AudioAuditResult(
                    AudioAuditVerdict.Incomplete,
                    $"Only fragments of the title or the author were heard, and {AudioCompleteness.Describe(tooShort, recordedMinutes!.Value, measuredMinutes!.Value)}.",
                    titleScore, authorScore, narratorScore, credits);
            }

            return new AudioAuditResult(
                AudioAuditVerdict.Inconclusive,
                "Only fragments of the title or the author were heard.",
                titleScore, authorScore, narratorScore, credits);
        }

        /// <summary>
        /// Whether two titles name the same book, allowing for the slack the transcriber
        /// and a shop's subtitle both introduce: "Ironclads" and "Iron Clads", "Ender's
        /// Shadow" and "Ender's Shadow: The Shadow Series, Book 1".
        /// </summary>
        public static bool SameTitle(string? wanted, string? other, double threshold = 0.6) =>
            !string.IsNullOrWhiteSpace(wanted)
            && !string.IsNullOrWhiteSpace(other)
            && Containment(Tokens(wanted), Tokens(other)) >= threshold;

        /// <summary>
        /// The share of the wanted tokens heard in order within a short window, with an
        /// edit of slack per word. A phrase, not a bag: "the war had gone on and the
        /// gifts were few" contains every word of "A War of Gifts" and is not it.
        /// </summary>
        internal static double Containment(string[] wanted, string[] heard)
        {
            var significant = wanted.Where(word => !StopWords.Contains(word)).ToArray();
            if (significant.Length == 0)
            {
                significant = wanted;
            }

            if (significant.Length == 0 || heard.Length == 0)
            {
                return 0;
            }

            var window = significant.Length * 2 + 2;
            var best = 0;
            for (var start = 0; start < heard.Length; start++)
            {
                var matched = 0;
                var position = start;
                var end = Math.Min(heard.Length, start + window);
                foreach (var word in significant)
                {
                    var hit = -1;
                    var consumed = 1;
                    for (var index = position; index < end; index++)
                    {
                        if (Close(word, heard[index]))
                        {
                            hit = index;
                            consumed = 1;
                            break;
                        }

                        // A compound the narrator says as one word and the transcriber
                        // writes as two, or the reverse: "Ironclads" heard as "Iron Clads".
                        // Nothing else in the loop can bridge a word boundary, so the whole
                        // title fails on a space.
                        if (index + 1 < end && Close(word, heard[index] + heard[index + 1]))
                        {
                            hit = index;
                            consumed = 2;
                            break;
                        }
                    }

                    if (hit >= 0)
                    {
                        matched++;
                        position = hit + consumed;
                    }
                }

                best = Math.Max(best, matched);
                if (best == significant.Length)
                {
                    break;
                }
            }

            return (double)best / significant.Length;
        }

        private static double BestName(IReadOnlyList<string>? names, IReadOnlyList<AuthorAlias>? aliases, string[] heard)
        {
            if (names == null || names.Count == 0)
            {
                return 0;
            }

            var best = 0.0;
            foreach (var name in names)
            {
                foreach (var spelling in Spellings(name, aliases))
                {
                    best = Math.Max(best, Containment(Tokens(spelling), heard));
                }
            }

            return best;
        }

        /// <summary>The name and every alias that maps to or from it.</summary>
        private static IEnumerable<string> Spellings(string name, IReadOnlyList<AuthorAlias>? aliases)
        {
            yield return name;
            if (aliases == null)
            {
                yield break;
            }

            foreach (var alias in aliases)
            {
                if (string.Equals(alias.Canonical, name, StringComparison.OrdinalIgnoreCase))
                {
                    yield return alias.Variant;
                }
                else if (string.Equals(alias.Variant, name, StringComparison.OrdinalIgnoreCase))
                {
                    yield return alias.Canonical;
                }
            }
        }

        /// <summary>
        /// Equal, or near enough that the difference is a mishearing rather than a
        /// different word.
        ///
        /// <para>
        /// The slack grows with the word, because one edit in five letters is most of the
        /// word and one edit in eleven is a syllable. A long name is where the transcriber
        /// actually goes wrong - "Tchaikovsky" comes back as "Chikovsky", two edits, and a
        /// flat one-edit rule called that a different author and flagged the book.
        /// </para>
        /// </summary>
        internal static bool Close(string wanted, string heard)
        {
            if (string.Equals(wanted, heard, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (wanted.Length < 4)
            {
                return false;
            }

            var slack = Slack(wanted.Length);
            if (Math.Abs(wanted.Length - heard.Length) > slack)
            {
                return false;
            }

            return Edits(wanted.ToLowerInvariant(), heard.ToLowerInvariant()) <= slack;
        }

        /// <summary>How many edits a word of this length may absorb and still be itself.</summary>
        internal static int Slack(int length) => length >= 8 ? 2 : 1;

        private static int Edits(string a, string b)
        {
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++)
            {
                previous[j] = j;
            }

            for (var i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }

                (previous, current) = (current, previous);
            }

            return previous[b.Length];
        }

        internal static string[] Tokens(string text) =>
            Punctuation().Replace(text.Replace('\n', ' '), " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => word.Trim('\'').ToLowerInvariant())
                .Where(word => word.Length > 0)
                .ToArray();

        private static string Describe(string? title, IReadOnlyList<string>? authors) =>
            authors is { Count: > 0 } ? $"\"{title}\" by {Join(authors)}" : $"\"{title}\"";

        private static string Join(IReadOnlyList<string> names) => string.Join(" / ", names);
    }
}
