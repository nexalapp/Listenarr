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
    public sealed partial class SuggestionService(
        IAudiobookRepository repository,
        IAuthorMonitoringService authorMonitoring,
        ISeriesMonitoringService seriesMonitoring,
        IConfigurationService configuration,
        ISuggestionReleaseRepository? releases = null) : ISuggestionService
    {
        public async Task<SuggestionSnapshot> GetAsync(CancellationToken cancellationToken = default)
        {
            var settings = await configuration.GetApplicationSettingsAsync();
            var languages = LanguageFilter.Parse(LanguageFilter.FromSettings(settings));
            // The operator's own list of spellings for one person, which until now the
            // page did not consult: a library stored under the canonical name was being
            // offered the variant as a book it lacked.
            var aliases = AuthorAliases.Parse(settings.AuthorAliasesJson);

            var monitoredAuthors = (await authorMonitoring.GetAllMonitoredAuthorsAsync(cancellationToken))
                .Select(author => SuggestionNames.Normalize(author.AuthorName))
                .ToHashSet(StringComparer.Ordinal);
            var monitoredSeries = (await seriesMonitoring.GetAllMonitoredSeriesAsync(cancellationToken))
                .Select(series => SuggestionNames.Normalize(series.SeriesName))
                .ToHashSet(StringComparer.Ordinal);

            var library = await repository.GetAllAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var memberships = await repository.GetAllSeriesMembershipsGroupedByAudiobookIdAsync(cancellationToken);
            var dismissals = await repository.GetSuggestionDismissalsAsync(cancellationToken);
            var held = new HeldBooks(library, memberships, dismissals.Select(d => d.Key), languages, aliases);

            IReadOnlyDictionary<string, SuggestionRelease> checks = releases == null
                ? new Dictionary<string, SuggestionRelease>()
                : await releases.GetAllAsync(cancellationToken);
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

                var authorMatchKey = SuggestionNames.AuthorMatchKey(entry.AuthorName);

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
                    // to; those are not "their books you are missing". The comparison
                    // forgives an initial, because a catalogue credits one edition to
                    // "Richard Morgan" and the next to "Richard K. Morgan", and the
                    // strict form dropped half an author's own books here.
                    .Where(book => book.Authors.Any(name =>
                        SuggestionNames.Normalize(name) == key
                        || SuggestionNames.AuthorMatchKey(name) == authorMatchKey))
                    .Where(book => !held.Contains(book.Asin, book.Isbn, book.Title, book.Authors))
                    // Another publication of a book already held — a re-release, a second
                    // narration, a retitled tie-in — is not a book anyone is missing.
                    .Where(book => !held.CoversSameStory(book.Title, book.Authors, book.Series, book.SeriesNumber))
                    .Where(book => !held.Ignored(book.Asin, book.Title, book.Authors))
                    .Select(Map)
                    .Select(book => WithRelease(book, checks))
                    .ToList();
                DistinctEditions(missing, aliases);

                if (missing.Count > 0)
                {
                    authorGroups.Add(new AuthorSuggestionGroup(
                        entry.AuthorName,
                        entry.AuthorAsin,
                        entry.ImageUrl,
                        libraryCount,
                        monitoredAuthors.Contains(key),
                        missing));
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
                    // As above: the shelf answers for it already, under another cover.
                    .Where(book => !held.CoversSameStory(book.Title, book.Authors, book.Series, book.SeriesNumber))
                    .Where(book => !held.Ignored(book.Asin, book.Title, book.Authors))
                    .Select(Map)
                    .Select(book => WithRelease(book, checks))
                    .OrderBy(book => SeriesPosition(book.SeriesNumber))
                    .ToList();
                DistinctEditions(missing, aliases);

                if (missing.Count > 0)
                {
                    seriesGroups.Add(new SeriesSuggestionGroup(
                        entry.SeriesName, entry.SeriesAsin, libraryCount, monitoredSeries.Contains(key), missing));
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
                    seriesWithCatalog.Count),
                dismissals
                    .Select(d => new IgnoredSuggestion(d.Key, d.Title, d.Author, d.DismissedAt))
                    .ToList(),
                ReleaseCoverage(authorGroups, seriesGroups));
        }

        public async Task<string> IgnoreAsync(IgnoreSuggestionRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var key = SuggestionNames.DismissalKey(request.Asin, request.Title, request.Authors)
                ?? throw new ArgumentException("A book needs an ASIN or a title and author to be ignored.", nameof(request));

            await repository.AddSuggestionDismissalAsync(
                new SuggestionDismissal
                {
                    Key = key,
                    Title = request.Title.Trim(),
                    Author = request.Authors?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))?.Trim()
                },
                cancellationToken);
            return key;
        }

        public Task<bool> RestoreAsync(string key, CancellationToken cancellationToken = default) =>
            repository.RemoveSuggestionDismissalAsync(key, cancellationToken);

        /// <summary>
        /// One card per book, not per edition: a catalog lists every narration and
        /// re-release, and offering "Childhood's End" four times is noise.
        /// </summary>
        private static void DistinctEditions(List<SuggestedBook> books, IReadOnlyList<AuthorAlias> aliases)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            books.RemoveAll(book =>
                (!string.IsNullOrWhiteSpace(book.Asin) && !seen.Add("asin:" + book.Asin.Trim()))
                // The match key, not the stored one: a catalogue that credits one
                // edition to "Richard Morgan" and the next to "Richard K. Morgan" was
                // offering both as separate books.
                || (SuggestionNames.MatchKey(book.Title, book.Authors, aliases) is { } key && !seen.Add("key:" + key)));
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
            book.RatingOverall, book.RatingCount, book.RatingStory, book.Description);

        private static SuggestedBook Map(CachedSeriesCatalogBook book) => new(
            book.Asin, book.Title, book.Subtitle, book.Authors, book.Narrators, book.ImageUrl,
            book.Runtime, book.Language, book.Publisher, book.Genres, book.Series,
            book.SeriesNumber, book.PublishedDate, book.Isbn, book.Link, book.MetadataSource,
            book.RatingOverall, book.RatingCount, book.RatingStory, book.Description);
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
            // Initials are joined so "B. V. Larson", "B.V. Larson" and "BV Larson" compare
            // equal: Audible credits the same author all three ways. This is a comparison
            // key only; the stored name is whatever the alias setting says.
            var parts = cleaned.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            var joined = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                // A one-letter token after a run of one-letter tokens is another initial.
                if (part.Length == 1 && joined.Count > 0 && joined[^1].Length <= 2)
                {
                    joined[^1] += part;
                }
                else
                {
                    joined.Add(part);
                }
            }

            return string.Join(' ', joined).ToLowerInvariant();
        }

        /// <summary>The key a dismissal is stored under: the ASIN when there is one.</summary>
        public static string? DismissalKey(string? asin, string? title, IReadOnlyList<string>? authors)
        {
            if (!string.IsNullOrWhiteSpace(asin))
            {
                return "asin:" + asin.Trim();
            }

            var key = TitleAuthorKey(title, authors);
            return key == null ? null : "key:" + key;
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

        /// <summary>
        /// The key two records are judged to be the same book by.
        ///
        /// <para>
        /// Looser than <see cref="TitleAuthorKey"/> in two ways, and deliberately kept
        /// apart from it because that one is written into the database as a dismissal
        /// and must never change meaning. Here the operator's name aliases are applied
        /// first, and then a middle initial is dropped: Audible credits "Altered Carbon"
        /// to "Richard Morgan" and "Broken Angels" to "Richard K. Morgan", and a library
        /// holding one of them was being offered the other as a book it lacked.
        /// </para>
        /// <para>
        /// The initial is dropped only while two real name parts remain, so an author
        /// who writes under initials keeps them: "M. R. Carey" stays "mr carey" rather
        /// than collapsing to "carey", where it would meet every other Carey.
        /// </para>
        /// </summary>
        public static string? MatchKey(
            string? title,
            IEnumerable<string>? authors,
            IReadOnlyList<AuthorAlias>? aliases = null)
        {
            var titleKey = StoryIdentity.TitleKey(title);
            if (titleKey.Length == 0)
            {
                return null;
            }

            var author = authors?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            if (author is null)
            {
                return null;
            }

            if (aliases is { Count: > 0 })
            {
                author = AuthorAliases.Apply([author], aliases).FirstOrDefault() ?? author;
            }

            var authorKey = AuthorMatchKey(author);
            return authorKey.Length == 0 ? null : $"{titleKey}|{authorKey}";
        }

        /// <summary>
        /// An author's name with the initials dropped, where a name remains without
        /// them. "Richard K. Morgan" and "James S. A. Corey" give "richard morgan" and
        /// "james corey"; "B. V. Larson" and "M. R. Carey" keep theirs, because
        /// "larson" and "carey" alone would meet every other Larson and Carey.
        ///
        /// <para>
        /// What marks an initial is the full stop, not the length: "Ed McBain" and "Jo
        /// Nesbo" are names. A part with no stop is kept whatever its length, so an
        /// author who is credited "BV Larson" is reached through
        /// <see cref="Normalize"/>, which joins initials, rather than through here.
        /// </para>
        /// </summary>
        public static string AuthorMatchKey(string? author)
        {
            var parts = (author ?? string.Empty)
                .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            var named = parts.Where(part => !IsInitials(part)).ToArray();
            return Normalize(string.Join(' ', named.Length >= 2 ? named : parts));
        }

        /// <summary>
        /// Whether a part of a name is initials: a lone letter, or letters each
        /// followed by a stop — "K", "K.", "S.A.".
        /// </summary>
        private static bool IsInitials(string part)
        {
            var trimmed = part.Trim().TrimEnd(',');
            if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
            {
                return true;
            }

            if (trimmed.Length < 2 || !trimmed.Contains('.'))
            {
                return false;
            }

            for (var index = 0; index < trimmed.Length; index += 2)
            {
                if (!char.IsLetter(trimmed[index])
                    || index + 1 >= trimmed.Length
                    || trimmed[index + 1] != '.')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
