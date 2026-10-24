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

namespace Listenarr.Application.Audiobooks.Suggestions
{
    /// <summary>
    /// Whether an indexer's release is, with high confidence, a suggested book.
    ///
    /// An abook.link post that declares an ASIN is held to it and nothing else: a matching
    /// title is not enough when the post says which edition it is. Most posts, the older
    /// ones especially, declare none, but their title and author are parsed fields rather
    /// than free text, so the title must equal the book's and the author must carry its
    /// first author's surname. A Newznab release only has a free-text title, so it must
    /// carry the book's title as a phrase and the surname as a word - the two things a
    /// release of some other book is least likely to share.
    /// </summary>
    public static partial class SuggestionReleaseMatch
    {
        [GeneratedRegex(@"[^\p{L}\p{N}]+")]
        private static partial Regex Separators();

        public static bool AsinMatches(string? releaseAsin, string? bookAsin) =>
            !string.IsNullOrWhiteSpace(releaseAsin)
            && !string.IsNullOrWhiteSpace(bookAsin)
            && string.Equals(releaseAsin.Trim(), bookAsin.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether an abook.link post, as its parsed fields describe it, is this book.</summary>
        public static bool AbookPostMatches(
            string? postAsin,
            string? postTitle,
            string? postAuthor,
            string? bookAsin,
            string? bookTitle,
            IReadOnlyList<string>? authors)
        {
            if (!string.IsNullOrWhiteSpace(postAsin))
            {
                return AsinMatches(postAsin, bookAsin);
            }

            var title = Normalize(MainTitle(bookTitle));
            var surname = Surname(authors?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)));
            return title.Length > 0
                && surname.Length > 0
                && Normalize(MainTitle(postTitle)) == title
                && (" " + Normalize(postAuthor) + " ").Contains(" " + surname + " ", StringComparison.Ordinal);
        }

        public static bool TitleAndAuthorMatch(string? releaseTitle, string? bookTitle, IReadOnlyList<string>? authors)
        {
            var release = " " + Normalize(releaseTitle) + " ";
            var title = Normalize(MainTitle(bookTitle));
            var surname = Surname(authors?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)));

            return title.Length > 0
                && surname.Length > 0
                && release.Contains(" " + title + " ", StringComparison.Ordinal)
                && release.Contains(" " + surname + " ", StringComparison.Ordinal);
        }

        /// <summary>What to search for: the main title and the first author.</summary>
        public static string QueryFor(string title, IReadOnlyList<string>? authors) =>
            $"{MainTitle(title)} {authors?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))}".Trim();

        /// <summary>
        /// The title before any subtitle: "Solaris: The Definitive Edition" is posted as
        /// "Solaris" as often as not, and a subtitle is no part of which book it is.
        /// </summary>
        private static string MainTitle(string? title)
        {
            var value = (title ?? string.Empty).Trim();
            var colon = value.IndexOf(':');
            return colon > 0 ? value[..colon].Trim() : value;
        }

        private static string Surname(string? author)
        {
            var words = Normalize(author).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 0 ? string.Empty : words[^1];
        }

        private static string Normalize(string? value) =>
            string.Join(' ', Separators()
                .Split((value ?? string.Empty).ToLowerInvariant())
                .Where(word => word.Length > 0));
    }
}
