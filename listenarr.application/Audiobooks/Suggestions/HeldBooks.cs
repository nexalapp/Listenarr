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
using Listenarr.Domain.Audiobooks.Catalog;

namespace Listenarr.Application.Audiobooks.Suggestions
{
    /// <summary>The library, indexed every way a catalog book might match it.</summary>
    public sealed class HeldBooks
    {
        private readonly HashSet<string> _asins = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _isbns = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _titleAuthorKeys = new(StringComparer.Ordinal);

        /// <summary>
        /// Every story the shelf answers for, by place in a series and by title. A
        /// catalogue entry matching one of these is another publication of a book
        /// already held, not a book missing from it.
        /// </summary>
        private readonly HashSet<string> _stories = new(StringComparer.Ordinal);

        private readonly IReadOnlySet<string>? _languages;
        private readonly HashSet<string> _ignored;

        public Dictionary<string, int> AuthorCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> SeriesCounts { get; } = new(StringComparer.Ordinal);

        public HeldBooks(
            IEnumerable<Audiobook> library,
            IReadOnlyDictionary<int, List<AudiobookSeriesMembership>> memberships,
            IEnumerable<string> ignoredKeys,
            IReadOnlySet<string>? languages)
        {
            _ignored = new HashSet<string>(ignoredKeys, StringComparer.Ordinal);
            _languages = languages;
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

                foreach (var story in StoriesHeldBy(book, memberships))
                {
                    _stories.Add(story);
                }
            }
        }

        /// <summary>
        /// Whether a catalog book is in one of the configured library languages. A
        /// catalog lists every translation, and an English library does not want the
        /// Italian one. A book with no language recorded passes; a null set means
        /// the user chose no filter.
        /// </summary>
        public bool SpeaksLanguage(string? language)
        {
            var normalized = AuthorCatalogMapping.NormalizeLanguage(language);
            return normalized == null || _languages == null || _languages.Contains(normalized);
        }

        /// <summary>
        /// The stories one library book stands for: its place in each series it
        /// belongs to, and its title. A book in two series is held at both places.
        /// </summary>
        private static IEnumerable<string> StoriesHeldBy(
            Audiobook book,
            IReadOnlyDictionary<int, List<AudiobookSeriesMembership>> memberships)
        {
            var rows = memberships.TryGetValue(book.Id, out var found) ? found : [];
            foreach (var row in rows)
            {
                if (StoryIdentity.PositionKey(row.SeriesName, row.SeriesNumber) is { } key)
                {
                    yield return key;
                }
            }

            if (rows.Count == 0 && StoryIdentity.PositionKey(book.Series, book.SeriesNumber) is { } primary)
            {
                yield return primary;
            }

            if (StoryIdentity.TitleAuthorKey(book.Title, book.Authors) is { } story)
            {
                yield return story;
            }
        }

        /// <summary>
        /// Whether the shelf already answers for this catalogue entry's story, by a
        /// publication that is not the same edition: the same place in the same
        /// series, or the same title by the same author.
        /// </summary>
        public bool CoversSameStory(string? title, IReadOnlyList<string>? authors, string? series, string? seriesNumber) =>
            StoryIdentity.KeysFor(title, authors, series, seriesNumber).Any(_stories.Contains);

        /// <summary>Dismissed by either identity: the ASIN, or the title and author.</summary>
        public bool Ignored(string? asin, string? title, IReadOnlyList<string>? authors)
        {
            if (_ignored.Count == 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(asin) && _ignored.Contains("asin:" + asin.Trim()))
            {
                return true;
            }

            var key = SuggestionNames.TitleAuthorKey(title, authors);
            return key != null && _ignored.Contains("key:" + key);
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
