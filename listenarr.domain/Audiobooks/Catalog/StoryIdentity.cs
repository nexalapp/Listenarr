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

namespace Listenarr.Domain.Audiobooks.Catalog
{
    /// <summary>
    /// What makes two catalogue entries the same story.
    ///
    /// <para>
    /// A catalogue holds more than stories: a re-release with new cover art, a second
    /// narration, an edition retitled for a television series. Each is its own ASIN, so
    /// each reads as a book the library lacks — which is wrong twice over, since the
    /// shelf already answers for it and nobody wants it suggested as missing.
    /// </para>
    /// <para>
    /// The comparison is deliberately loose, because publications differ in exactly the
    /// ways a strict one would trip over: the same place in the same series is the same
    /// story, and so is the same title once the packaging is stripped off. Narrator,
    /// runtime and year are not compared — a different narration of a book someone owns
    /// is still that book.
    /// </para>
    /// </summary>
    public static partial class StoryIdentity
    {
        /// <summary>
        /// The words a publisher adds around a title, which two publications of one story
        /// rarely agree on.
        /// </summary>
        [GeneratedRegex(
            @"\b(unabridged|abridged|audiobook|audio book|dramatized|dramatised|adaptation|a novel|a novella|special edition|collectors edition|collector s edition|anniversary edition|deluxe edition|movie tie in|tv tie in|media tie in|tie in edition|new edition|revised edition|reissue|remastered)\b",
            RegexOptions.IgnoreCase)]
        private static partial Regex Packaging();

        [GeneratedRegex(@"^\s*\d+(\.\d+)?\s*$")]
        private static partial Regex PlainNumber();

        /// <summary>Lower case, letters and digits only, spaces collapsed.</summary>
        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var cleaned = new string(value
                .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')
                .ToArray());
            return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// A title reduced to the story it names: the subtitle dropped, the packaging
        /// stripped. "Wool", "Wool: The Silo Saga" and "Wool (Unabridged)" all give
        /// "wool".
        /// </summary>
        public static string TitleKey(string? title)
        {
            // The subtitle goes first, while the punctuation that marks it still exists.
            var head = (title ?? string.Empty).Split([':', '–', '—'], 2)[0];
            var normalized = Normalize(head);
            var stripped = string.Join(
                ' ',
                Packaging().Replace(normalized, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

            // A title that is nothing but packaging keeps its words rather than vanishing.
            return stripped.Length > 0 ? stripped : normalized;
        }

        /// <summary>
        /// A place in a series, when the entry names one. An omnibus — "1-3" — is not a
        /// place, and stands in for no single story.
        /// </summary>
        public static string? PositionKey(string? seriesName, string? seriesNumber)
        {
            var series = Normalize(seriesName);
            if (series.Length == 0 || string.IsNullOrWhiteSpace(seriesNumber) || !PlainNumber().IsMatch(seriesNumber))
            {
                return null;
            }

            return decimal.TryParse(
                seriesNumber.Trim(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var position)
                ? $"position:{series}:{position}"
                : null;
        }

        /// <summary>
        /// The key a story is recognised by when its place in a series is not known: its
        /// title and who wrote it, so two authors' books of the same name stay apart.
        /// </summary>
        public static string? TitleAuthorKey(string? title, IEnumerable<string>? authors)
        {
            var title_ = TitleKey(title);
            if (title_.Length == 0)
            {
                return null;
            }

            var author = string.Join(
                '|',
                (authors ?? [])
                    .Select(Normalize)
                    .Where(name => name.Length > 0)
                    .OrderBy(name => name, StringComparer.Ordinal));

            return author.Length == 0 ? null : $"story:{title_}::{author}";
        }

        /// <summary>Every key this entry would be recognised by; empty when it names nothing usable.</summary>
        public static IEnumerable<string> KeysFor(string? title, IEnumerable<string>? authors, string? seriesName, string? seriesNumber)
        {
            if (PositionKey(seriesName, seriesNumber) is { } position)
            {
                yield return position;
            }

            if (TitleAuthorKey(title, authors) is { } story)
            {
                yield return story;
            }
        }
    }
}
