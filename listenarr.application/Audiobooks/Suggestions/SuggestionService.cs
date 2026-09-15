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
    /// <summary>
    /// Works out what the library is missing from the catalogs it has already cached.
    ///
    /// <para>
    /// A catalog book counts as held when the library has its ASIN, one of its ISBNs, or
    /// a book with the same title by the same first author. That last rule is looser than
    /// the one the add path uses on purpose: the add path must not refuse a second
    /// narration, but this page must not offer one, because "you are missing this" about
    /// a book already on the shelf is the fastest way to make the page ignorable.
    /// </para>
    /// </summary>
    public sealed class SuggestionService(IAudiobookRepository repository) : ISuggestionService
    {
        public async Task<SuggestionSnapshot> GetAsync(CancellationToken cancellationToken = default)
        {
            var library = await repository.GetAllAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var memberships = await repository.GetAllSeriesMembershipsGroupedByAudiobookIdAsync(cancellationToken);
            var held = new HeldBooks(library, memberships);

            var cachedAuthors = await repository.GetAllCachedAuthorsAsync(cancellationToken);
            var cachedSeries = await repository.GetAllCachedSeriesAsync(cancellationToken);

            var authorGroups = new List<AuthorSuggestionGroup>();
            var authorsWithCatalog = new HashSet<string>(StringComparer.Ordinal);
            var related = new Dictionary<string, (string Name, string? Asin, List<string> Because)>(
                StringComparer.Ordinal);

            foreach (var entry in cachedAuthors)
            {
                var key = SuggestionNames.Normalize(entry.AuthorName);
                if (!held.AuthorCounts.TryGetValue(key, out var libraryCount))
                {
                    continue;
                }

                foreach (var similar in entry.SimilarAuthors ?? [])
                {
                    var similarKey = SuggestionNames.Normalize(similar.Name);
                    if (similarKey.Length == 0 || held.AuthorCounts.ContainsKey(similarKey))
                    {
                        continue;
                    }

                    if (!related.TryGetValue(similarKey, out var existing))
                    {
                        existing = (similar.Name, similar.Asin, []);
                        related[similarKey] = existing;
                    }

                    if (!existing.Because.Contains(entry.AuthorName))
                    {
                        existing.Because.Add(entry.AuthorName);
                    }
                }

                if (entry.CatalogBooks is not { Count: > 0 })
                {
                    continue;
                }

                authorsWithCatalog.Add(key);
                var missing = entry.CatalogBooks
                    .Where(book => held.SpeaksLanguage(book.Language))
                    // An author's catalog includes anthologies they contributed a story
                    // to; those are not "their books you are missing".
                    .Where(book => book.Authors.Any(name => SuggestionNames.Normalize(name) == key))
                    .Where(book => !held.Contains(book.Asin, book.Isbn, book.Title, book.Authors))
                    .Select(Map)
                    .ToList();
                DistinctEditions(missing);

                if (missing.Count > 0)
                {
                    authorGroups.Add(new AuthorSuggestionGroup(
                        entry.AuthorName, entry.AuthorAsin, entry.ImageUrl, libraryCount, missing));
                }
            }

            var seriesGroups = new List<SeriesSuggestionGroup>();
            var seriesWithCatalog = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in cachedSeries)
            {
                var key = SuggestionNames.Normalize(entry.SeriesName);
                if (!held.SeriesCounts.TryGetValue(key, out var libraryCount)
                    || entry.CatalogBooks is not { Count: > 0 })
                {
                    continue;
                }

                seriesWithCatalog.Add(key);
                var missing = entry.CatalogBooks
                    .Where(book => held.SpeaksLanguage(book.Language))
                    .Where(book => !held.Contains(book.Asin, book.Isbn, book.Title, book.Authors))
                    .Select(Map)
                    .OrderBy(book => SeriesPosition(book.SeriesNumber))
                    .ToList();
                DistinctEditions(missing);

                if (missing.Count > 0)
                {
                    seriesGroups.Add(new SeriesSuggestionGroup(
                        entry.SeriesName, entry.SeriesAsin, libraryCount, missing));
                }
            }

            return new SuggestionSnapshot(
                authorGroups
                    .OrderByDescending(group => group.LibraryCount)
                    .ThenBy(group => group.Author, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                seriesGroups
                    .OrderByDescending(group => group.LibraryCount)
                    .ThenBy(group => group.Series, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                related.Values
                    .OrderByDescending(author => author.Because.Count)
                    .ThenBy(author => author.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(author => new RelatedAuthorSuggestion(author.Name, author.Asin, author.Because))
                    .ToList(),
                new SuggestionCoverage(
                    held.AuthorCounts.Count,
                    authorsWithCatalog.Count,
                    held.SeriesCounts.Count,
                    seriesWithCatalog.Count));
        }

        /// <summary>
        /// One card per book, not per edition: a catalog lists every narration and
        /// re-release, and offering "Childhood's End" four times is noise.
        /// </summary>
        private static void DistinctEditions(List<SuggestedBook> books)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            books.RemoveAll(book =>
                (!string.IsNullOrWhiteSpace(book.Asin) && !seen.Add("asin:" + book.Asin.Trim()))
                || (SuggestionNames.TitleAuthorKey(book.Title, book.Authors) is { } key && !seen.Add("key:" + key)));
        }

        private static decimal SeriesPosition(string? value) =>
            decimal.TryParse(
                (value ?? string.Empty).Trim(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number)
                ? number
                : decimal.MaxValue;

        private static SuggestedBook Map(CachedAuthorCatalogBook book) => new(
            book.Asin, book.Title, book.Subtitle, book.Authors, book.Narrators, book.ImageUrl,
            book.Runtime, book.Language, book.Publisher, book.Genres, book.Series,
            book.SeriesNumber, book.PublishedDate, book.Isbn, book.Link, book.MetadataSource,
            book.RatingOverall, book.RatingCount, book.RatingStory);

        private static SuggestedBook Map(CachedSeriesCatalogBook book) => new(
            book.Asin, book.Title, book.Subtitle, book.Authors, book.Narrators, book.ImageUrl,
            book.Runtime, book.Language, book.Publisher, book.Genres, book.Series,
            book.SeriesNumber, book.PublishedDate, book.Isbn, book.Link, book.MetadataSource,
            book.RatingOverall, book.RatingCount, book.RatingStory);

        /// <summary>The library, indexed every way a catalog book might match it.</summary>
        private sealed class HeldBooks
        {
            private readonly HashSet<string> _asins = new(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _isbns = new(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _titleAuthorKeys = new(StringComparer.Ordinal);

            private readonly HashSet<string> _languages = new(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, int> AuthorCounts { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, int> SeriesCounts { get; } = new(StringComparer.Ordinal);

            public HeldBooks(
                IEnumerable<Audiobook> library,
                IReadOnlyDictionary<int, List<AudiobookSeriesMembership>> memberships)
            {
                foreach (var book in library)
                {
                    if (!string.IsNullOrWhiteSpace(book.Asin))
                    {
                        _asins.Add(book.Asin.Trim());
                    }

                    foreach (var isbn in book.Isbn ?? [])
                    {
                        var digits = SuggestionNames.Digits(isbn);
                        if (digits.Length > 0)
                        {
                            _isbns.Add(digits);
                        }
                    }

                    var key = SuggestionNames.TitleAuthorKey(book.Title, book.Authors);
                    if (key != null)
                    {
                        _titleAuthorKeys.Add(key);
                    }

                    var language = AuthorCatalogMapping.NormalizeLanguage(book.Language);
                    if (language != null)
                    {
                        _languages.Add(language);
                    }

                    foreach (var author in book.Authors ?? [])
                    {
                        Count(AuthorCounts, author);
                    }

                    var seriesNames = new HashSet<string>(StringComparer.Ordinal);
                    if (memberships.TryGetValue(book.Id, out var rows))
                    {
                        foreach (var row in rows)
                        {
                            seriesNames.Add(SuggestionNames.Normalize(row.SeriesName));
                        }
                    }

                    seriesNames.Add(SuggestionNames.Normalize(book.Series));
                    foreach (var series in seriesNames.Where(name => name.Length > 0))
                    {
                        CountKey(SeriesCounts, series);
                    }
                }
            }

            /// <summary>
            /// Whether a catalog book is in a language the library already reads. A
            /// catalog lists every translation, and a library with nothing but English
            /// does not want the Italian one. A book with no language recorded passes,
            /// as does everything when the library has no languages recorded.
            /// </summary>
            public bool SpeaksLanguage(string? language)
            {
                var normalized = AuthorCatalogMapping.NormalizeLanguage(language);
                return normalized == null || _languages.Count == 0 || _languages.Contains(normalized);
            }

            public bool Contains(string? asin, string? isbn, string? title, IReadOnlyList<string>? authors)
            {
                if (!string.IsNullOrWhiteSpace(asin) && _asins.Contains(asin.Trim()))
                {
                    return true;
                }

                var digits = SuggestionNames.Digits(isbn);
                if (digits.Length > 0 && _isbns.Contains(digits))
                {
                    return true;
                }

                var key = SuggestionNames.TitleAuthorKey(title, authors);
                return key != null && _titleAuthorKeys.Contains(key);
            }

            private static void Count(Dictionary<string, int> counts, string? name)
            {
                var key = SuggestionNames.Normalize(name);
                if (key.Length > 0)
                {
                    CountKey(counts, key);
                }
            }

            private static void CountKey(Dictionary<string, int> counts, string key) =>
                counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }
    }

    /// <summary>
    /// The same name normalisation the catalog caches are keyed by: letters, digits and
    /// single spaces, lower-cased. Anything else and a cached author would not be found
    /// for the library author it was fetched for.
    /// </summary>
    public static class SuggestionNames
    {
        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var cleaned = new string(value
                .Where(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
                .ToArray());
            return string.Join(
                ' ',
                cleaned.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
                .ToLowerInvariant();
        }

        public static string Digits(string? value) =>
            new((value ?? string.Empty).Where(char.IsDigit).ToArray());

        /// <summary>
        /// Title plus first author. The title is cut at a colon so "Foo: A Novel" and
        /// "Foo" meet, and the author is the first listed because catalog and library
        /// often disagree about co-authors and translators.
        /// </summary>
        public static string? TitleAuthorKey(string? title, IReadOnlyList<string>? authors)
        {
            var titleKey = Normalize((title ?? string.Empty).Split(':')[0]);
            var authorKey = Normalize(authors?.FirstOrDefault(author => !string.IsNullOrWhiteSpace(author)));
            return titleKey.Length == 0 || authorKey.Length == 0 ? null : $"{titleKey}|{authorKey}";
        }
    }
}
