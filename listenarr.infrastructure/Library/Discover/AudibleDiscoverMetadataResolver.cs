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
using Listenarr.Application.Audiobooks.Discover;
using Listenarr.Application.Audiobooks.Suggestions;
using Listenarr.Domain.Audiobooks.Catalog;
using Listenarr.Domain.Audiobooks.Discover;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Discover
{
    /// <summary>
    /// Looks up a book a forum post named, so a row posted as a line of text gets a
    /// cover, a rating and an ASIN the add flow can use.
    ///
    /// <para>
    /// The match has to be strict. A wrong hit here does not fail visibly — it shows the
    /// wrong cover and adds the wrong book — so a candidate is accepted only when its
    /// title reduces to the same story and its author agrees. A post whose title we
    /// cannot place keeps the words the poster wrote, which is still a usable row.
    /// </para>
    /// </summary>
    public sealed class AudibleDiscoverMetadataResolver(
        AudibleService audible,
        IConfigurationService configuration,
        ILogger<AudibleDiscoverMetadataResolver> logger) : IDiscoverMetadataResolver
    {
        public async Task<DiscoverBook?> ResolveAsync(
            DiscoverBook unresolved,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(unresolved);
            if (string.IsNullOrWhiteSpace(unresolved.Title))
            {
                return null;
            }

            var settings = await configuration.GetApplicationSettingsAsync();
            if (!settings.EnableAudibleSearch)
            {
                return null;
            }

            var region = AudibleRequestHelper.NormalizeRegion(settings.DefaultSearchRegion ?? "us");
            var author = unresolved.Authors?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

            try
            {
                // Keywords first, ranked by relevance, because that is the question
                // being asked: "the book called this, by them". The author-catalogue
                // search is the fallback, and only ever a fallback: an author with
                // three hundred titles answers it with whichever page of them Audible
                // feels like, and the book wanted is rarely on it.
                var keywords = string.IsNullOrWhiteSpace(author)
                    ? unresolved.Title
                    : $"{unresolved.Title} {author}";

                var match = Best(await audible.SearchBooksAsync(keywords, 1, 10, region), unresolved, author);
                if (match is null && !string.IsNullOrWhiteSpace(author))
                {
                    match = Best(
                        await audible.SearchByTitleAndAuthorAsync(unresolved.Title, author!, 1, 50, region),
                        unresolved,
                        author);
                }

                return match is null ? null : DiscoverMapping.FromSearchResult(match);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not look up '{Title}' for the discovery feed", unresolved.Title);
                return null;
            }
        }

        /// <summary>The best hit in a response, or null when none of them is the book.</summary>
        private static AudibleSearchResult? Best(
            AudibleSearchResponse? response,
            DiscoverBook wanted,
            string? author) =>
            (response?.Results ?? [])
                .Select(result => new { Result = result, Score = Score(result, wanted, author) })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Result)
                .FirstOrDefault();

        /// <summary>
        /// How well a hit answers the post, or zero when it does not.
        ///
        /// <para>
        /// The author has to agree, always: a wrong hit here shows the wrong cover and
        /// adds the wrong book, and nothing about that failure is visible. The title is
        /// the looser half, because the two sides write it differently — a forum title
        /// keeps the series in its own field and calls the book "Valiant", while the
        /// catalogue sells it as "The Lost Fleet: Valiant". So a catalogue title that
        /// contains the post's title, whole and on word boundaries, counts; a title
        /// that merely starts with the same letters does not.
        /// </para>
        /// </summary>
        private static int Score(AudibleSearchResult result, DiscoverBook wanted, string? author)
        {
            if (!AuthorAgrees(result, author))
            {
                return 0;
            }

            if (StoryIdentity.TitleKey(result.Title) == StoryIdentity.TitleKey(wanted.Title))
            {
                return SeriesAgrees(result, wanted) ? 4 : 3;
            }

            if (!Contains(SuggestionNames.Normalize(result.Title), SuggestionNames.Normalize(wanted.Title)))
            {
                return 0;
            }

            return SeriesAgrees(result, wanted) ? 2 : 1;
        }

        /// <summary>
        /// Whether the hit was written by whom the post said. A forum title writes
        /// "J. S. A. Corey" where the catalogue writes "James S. A. Corey", so one
        /// shared name part of real length is enough; initials are not.
        /// </summary>
        private static bool AuthorAgrees(AudibleSearchResult result, string? author)
        {
            if (string.IsNullOrWhiteSpace(author))
            {
                return true;
            }

            var wanted = Words(author).Where(part => part.Length > 2).ToList();
            if (wanted.Count == 0)
            {
                return true;
            }

            return (result.Authors ?? [])
                .Select(candidate => Words(candidate.Name))
                .Any(name => wanted.Any(part => name.Contains(part, StringComparer.Ordinal)));
        }

        /// <summary>
        /// Whether both name the same series. A tie-breaker only: the catalogue often
        /// names none, and a post whose series we cannot confirm is not thereby wrong.
        /// </summary>
        private static bool SeriesAgrees(AudibleSearchResult result, DiscoverBook wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted.Series))
            {
                return false;
            }

            var series = SuggestionNames.Normalize(wanted.Series);
            return (result.Series ?? [])
                .Any(candidate => SuggestionNames.Normalize(candidate.Name) == series);
        }

        /// <summary>Whether <paramref name="needle"/> appears in <paramref name="haystack"/> as whole words, in order.</summary>
        private static bool Contains(string haystack, string needle)
        {
            var words = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var all = haystack.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // One short word is not a title: "One" would match half the catalogue.
            if (words.Length == 0 || all.Length == 0 || (words.Length == 1 && words[0].Length < 4))
            {
                return false;
            }

            for (var start = 0; start + words.Length <= all.Length; start++)
            {
                if (words.Where((word, offset) => all[start + offset] != word).Any())
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static List<string> Words(string? value) =>
            SuggestionNames.Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
