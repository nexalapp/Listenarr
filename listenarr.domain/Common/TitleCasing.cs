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
namespace Listenarr.Domain.Common
{
    /// <summary>
    /// Raises a sentence-cased title to title case without ever lowering a letter.
    ///
    /// Open Library catalogues titles the way a library card does - "Fortress of owls",
    /// "The cat who walks through walls" - and every book matched there landed in the
    /// library with that casing. The rule is upgrade-only: a word that already carries a
    /// capital anywhere is the publisher's own styling ("al-Sirafi", "iPhone", "K.") and is
    /// left alone, so a title that was already right comes back unchanged. Small words stay
    /// lower except at the start, at the end, or right after a colon.
    /// </summary>
    public static class TitleCasing
    {
        private static readonly HashSet<string> SmallWords = new(StringComparer.Ordinal)
        {
            "a", "an", "the", "and", "but", "or", "nor", "for", "so", "yet",
            "of", "in", "on", "at", "to", "by", "up", "as", "vs", "via",
            "from", "with", "into", "onto", "over", "upon", "than", "per", "off",
        };

        private static readonly char[] TrailingPunctuation =
            [':', ';', ',', '.', '!', '?', ')', ']', '"', '\'', '\u201D', '\u2019'];

        public static string ToTitleCase(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return title ?? string.Empty;
            }

            var words = title.Split(' ');
            var lastIndex = Array.FindLastIndex(words, w => w.Length > 0);
            var afterBoundary = true;

            for (var i = 0; i < words.Length; i++)
            {
                var word = words[i];
                if (word.Length == 0)
                {
                    continue;
                }

                var core = word.TrimEnd(TrailingPunctuation);
                var isBoundaryWord = afterBoundary || i == lastIndex;
                afterBoundary = word.EndsWith(':');

                var first = IndexOfFirstLetter(core);
                if (first < 0 || core.Any(char.IsUpper))
                {
                    continue;
                }

                if (!isBoundaryWord && SmallWords.Contains(core))
                {
                    continue;
                }

                words[i] = word[..first] + char.ToUpperInvariant(word[first]) + word[(first + 1)..];
            }

            return string.Join(' ', words);
        }

        private static int IndexOfFirstLetter(string word)
        {
            for (var i = 0; i < word.Length; i++)
            {
                if (char.IsLetter(word[i]))
                {
                    return i;
                }

                // A leading digit is the word: "2010" and "33" are not to be touched, and a
                // title like "1984" is not a sentence-case slip.
                if (char.IsDigit(word[i]))
                {
                    return -1;
                }
            }

            return -1;
        }
    }
}
