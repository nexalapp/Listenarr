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
using System.Text;
using Listenarr.Domain.Common;

namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>
    /// How nearly two names are the same name, when one of them was transcribed from
    /// speech.
    ///
    /// <para>
    /// Whisper spells what it hears, so a credited narrator comes back respelled rather
    /// than replaced: Mikael Naramore as "Michael Narrowmore", Emily Woo Zeller as
    /// "Emily Wuzallar", P. J. Ochlan as "PJ Oakland". Letter distance alone does not
    /// separate those from a genuinely different reader - measured over 160 flagged
    /// books they overlap - because the respelling changes many letters while keeping
    /// the sound.
    /// </para>
    /// <para>
    /// So the comparison is three views blended: the letters, the sound of the whole
    /// name, and how many of its parts sound alike. Word boundaries move under
    /// transcription ("Woo Zeller" becomes one word), which is why the whole name is
    /// compared with its spaces removed as well as token by token.
    /// </para>
    /// </summary>
    public static class SpokenNameSimilarity
    {
        /// <summary>
        /// Above this, a heard name is the credited one said differently. Chosen against
        /// this library's own flagged books: every pair at or above it was the same
        /// reader, and the band below holds real differences - Stefan Rudnicki against
        /// Stephen Hoy, Tim Sample against Penny Sampell.
        /// </summary>
        public const double SameNameThreshold = 0.70;

        /// <summary>Whether a heard name is any of these names, allowing for transcription.</summary>
        public static bool IsAnyOf(string? heard, IEnumerable<string?>? names) =>
            Best(heard, names) >= SameNameThreshold;

        /// <summary>The closest of these names to the heard one, 0 to 1.</summary>
        public static double Best(string? heard, IEnumerable<string?>? names)
        {
            var spoken = Tokenize(heard);
            if (spoken.Count == 0)
            {
                return 0;
            }

            var best = 0.0;
            foreach (var name in names ?? [])
            {
                var known = Tokenize(name);
                if (known.Count == 0)
                {
                    continue;
                }

                var score = Compare(spoken, known);
                if (score > best)
                {
                    best = score;
                }
            }

            return best;
        }

        private static double Compare(List<string> spoken, List<string> known)
        {
            var spokenJoined = string.Concat(spoken);
            var knownJoined = string.Concat(known);

            var letters = Ratio(spokenJoined, knownJoined);
            var sound = Ratio(Soundex(spokenJoined), Soundex(knownJoined));

            // Parts that sound alike, over the shorter name: a heard name may drop a
            // middle name or merge two words into one.
            var spokenCodes = Codes(spoken);
            var knownCodes = Codes(known);
            var shared = spokenCodes.Count == 0 || knownCodes.Count == 0
                ? 0.0
                : (double)spokenCodes.Intersect(knownCodes, StringComparer.Ordinal).Count()
                    / Math.Min(spokenCodes.Count, knownCodes.Count);

            // The letters alone are enough when they agree; otherwise the three views
            // together, and the parts alone are never quite conclusive by themselves.
            return Math.Max(letters, Math.Max((letters + sound + shared) / 3, shared * 0.9));
        }

        private static HashSet<string> Codes(List<string> tokens) =>
            tokens.Where(t => t.Length > 2).Select(Soundex).Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);

        private static double Ratio(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0)
            {
                return 0;
            }

            return 1.0 - (double)StringUtils.LevenshteinDistance(a, b) / Math.Max(a.Length, b.Length);
        }

        /// <summary>Letters only, lowered, accents folded; initials and punctuation dropped.</summary>
        private static List<string> Tokenize(string? value)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return tokens;
            }

            var folded = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var c in folded)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetter(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (builder.Length > 0)
                {
                    tokens.Add(builder.ToString());
                    builder.Clear();
                }
            }

            if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
            }

            return tokens;
        }

        /// <summary>Soundex: the first letter and the sounds after it, as four characters.</summary>
        private static string Soundex(string word)
        {
            if (word.Length == 0)
            {
                return string.Empty;
            }

            var code = new StringBuilder();
            code.Append(char.ToUpperInvariant(word[0]));
            var previous = Digit(word[0]);
            foreach (var c in word.AsSpan(1))
            {
                var digit = Digit(c);
                if (digit != '\0' && digit != previous)
                {
                    code.Append(digit);
                    if (code.Length == 4)
                    {
                        break;
                    }
                }

                // H and W are transparent: the sound either side of them still counts as
                // repeated, which is what keeps "Ochlan" and "Oakland" together.
                if (c is not ('h' or 'w' or 'H' or 'W'))
                {
                    previous = digit;
                }
            }

            return code.Append("000").ToString()[..4];
        }

        private static char Digit(char c) => char.ToLowerInvariant(c) switch
        {
            'b' or 'f' or 'p' or 'v' => '1',
            'c' or 'g' or 'j' or 'k' or 'q' or 's' or 'x' or 'z' => '2',
            'd' or 't' => '3',
            'l' => '4',
            'm' or 'n' => '5',
            'r' => '6',
            _ => '\0'
        };
    }
}
