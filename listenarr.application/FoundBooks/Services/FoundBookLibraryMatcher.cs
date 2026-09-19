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
using Listenarr.Domain.Common;
using Listenarr.Domain.FoundBooks;

namespace Listenarr.Application.FoundBooks.Services
{
    public sealed record FoundBookLibraryMatch(FoundBookLibraryStatus Status, int? AudiobookId);

    /// <summary>
    /// Decides whether the library already holds a found book. Identifier first; then a
    /// title that agrees exactly, or nearly so for a long one, with an author that
    /// agrees. Deliberately conservative: a false "in library" hides a real book,
    /// while a false "new" costs one extra look at the catalogue.
    /// </summary>
    public static partial class FoundBookLibraryMatcher
    {
        [GeneratedRegex(@"\b(unabridged|abridged)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex EditionWords();

        private const int LongTitle = 12;

        public static FoundBookLibraryMatch Match(
            string? asin,
            string? title,
            string? author,
            IReadOnlyList<Audiobook> library)
        {
            ArgumentNullException.ThrowIfNull(library);

            Audiobook? match = null;
            if (!string.IsNullOrWhiteSpace(asin))
            {
                match = library.FirstOrDefault(book =>
                    string.Equals(book.Asin, asin, StringComparison.OrdinalIgnoreCase));
            }

            if (match == null)
            {
                var titleKey = TitleKey(title);
                if (titleKey.Length > 0)
                {
                    var authorKey = FileUtils.NormalizeComparisonValue(author);
                    match = library.FirstOrDefault(book => TitleMatches(titleKey, TitleKey(book.Title), authorKey, book));
                }
            }

            if (match == null)
            {
                return new FoundBookLibraryMatch(FoundBookLibraryStatus.New, null);
            }

            var hasFile = !string.IsNullOrWhiteSpace(match.FilePath)
                || (match.Files?.Count ?? 0) > 0;
            return hasFile
                ? new FoundBookLibraryMatch(FoundBookLibraryStatus.InLibrary, match.Id)
                : match.Monitored
                    ? new FoundBookLibraryMatch(FoundBookLibraryStatus.Wanted, match.Id)
                    : new FoundBookLibraryMatch(FoundBookLibraryStatus.New, null);
        }

        private static bool TitleMatches(string candidate, string held, string authorKey, Audiobook book)
        {
            if (held.Length == 0)
            {
                return false;
            }

            var exact = string.Equals(candidate, held, StringComparison.Ordinal);
            var near = !exact
                && Math.Min(candidate.Length, held.Length) >= LongTitle
                && (candidate.Contains(held, StringComparison.Ordinal) || held.Contains(candidate, StringComparison.Ordinal));
            if (!exact && !near)
            {
                return false;
            }

            var heldAuthors = book.Authors?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList() ?? [];
            if (authorKey.Length == 0 || heldAuthors.Count == 0)
            {
                // With an author missing on one side only an exact title is trusted.
                return exact;
            }

            return heldAuthors.Any(a => AuthorsAgree(FileUtils.NormalizeComparisonValue(a), authorKey));
        }

        /// <summary>
        /// "Heinlein, Robert A" and "Robert A. Heinlein" are one person: every word of
        /// the shorter name, initials aside, appears in the longer. Either side may
        /// carry a second name ("Adalyn Grace, Kristin Atherton") without breaking it.
        /// </summary>
        internal static bool AuthorsAgree(string left, string right)
        {
            var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 1).ToHashSet(StringComparer.Ordinal);
            var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 1).ToHashSet(StringComparer.Ordinal);
            if (leftTokens.Count == 0 || rightTokens.Count == 0)
            {
                return false;
            }

            return leftTokens.IsSubsetOf(rightTokens) || rightTokens.IsSubsetOf(leftTokens);
        }

        internal static string TitleKey(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var trimmed = EditionWords().Replace(title, " ");
            return FileUtils.NormalizeComparisonValue(trimmed);
        }
    }
}
