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

namespace Listenarr.Application.Audiobooks.Suggestions
{
    public sealed partial class SuggestionService
    {
        /// <summary>
        /// The book with what the background release check found for it, if anything yet.
        ///
        /// A check that learned nothing - every indexer failed to answer - is deliberately
        /// not reported as a result. The page hides a book whose release is "not found",
        /// and an indexer outage is not evidence that nobody has the book; left unreported
        /// the book reads as still unchecked, which is what it is.
        /// </summary>
        private static SuggestedBook WithRelease(SuggestedBook book, IReadOnlyDictionary<string, SuggestionRelease> checks) =>
            book.Asin is { Length: > 0 } asin
            && checks.TryGetValue(asin.Trim(), out var check)
            && check.Status != SuggestionReleaseStatus.Unknown
                ? book with
                {
                    Release = new SuggestedRelease(
                        check.Status == SuggestionReleaseStatus.Found,
                        check.IndexerName,
                        check.ReleaseTitle,
                        check.SizeBytes,
                        check.CheckedAt)
                }
                : book;

        /// <summary>Counts each suggested book once, however many groups list it.</summary>
        private static SuggestionReleaseCoverage ReleaseCoverage(
            IEnumerable<AuthorSuggestionGroup> authors,
            IEnumerable<SeriesSuggestionGroup> series)
        {
            var books = authors.SelectMany(group => group.Missing)
                .Concat(series.SelectMany(group => group.Missing))
                .Where(book => !string.IsNullOrWhiteSpace(book.Asin))
                .GroupBy(book => book.Asin!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            return new SuggestionReleaseCoverage(
                books.Count,
                books.Count(book => book.Release != null),
                books.Count(book => book.Release?.Found == true));
        }
    }
}
