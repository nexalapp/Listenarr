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

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Suggestions
{
    /// <summary>Checks suggested books for a release, and grabs a stored one.</summary>
    public interface ISuggestionReleaseService
    {
        /// <summary>Asks every enabled indexer for the book and stores what was found.</summary>
        Task<SuggestionRelease> CheckAsync(SuggestedBook book, CancellationToken cancellationToken = default);

        /// <summary>
        /// The same check for any book that carries an ASIN, suggested or already in the
        /// library. The question - does an indexer hold this book - is the same one, and
        /// the answer is stored against the ASIN either way.
        /// </summary>
        Task<SuggestionRelease> CheckAsync(
            string asin,
            string title,
            IReadOnlyList<string> authors,
            CancellationToken cancellationToken = default,
            string? seriesNumber = null);

        /// <summary>
        /// Sends the release stored for the book's ASIN to a download client. Returns false,
        /// having done nothing, when no release was found for it.
        /// </summary>
        Task<bool> GrabStoredAsync(Audiobook audiobook, CancellationToken cancellationToken = default);
    }

    public sealed class SuggestionReleaseService(
        ISearchService search,
        IIndexerRepository indexers,
        IQualityProfileService qualityProfiles,
        ISecretProtector secrets,
        ISuggestionReleaseRepository store,
        IDownloadService downloads,
        ILogger<SuggestionReleaseService> logger) : ISuggestionReleaseService
    {
        private const string AbookLinkImplementation = "AbookLink";

        public Task<SuggestionRelease> CheckAsync(SuggestedBook book, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(book);
            if (string.IsNullOrWhiteSpace(book.Asin))
            {
                throw new ArgumentException("A suggested book is checked by its ASIN.", nameof(book));
            }

            return CheckAsync(book.Asin, book.Title, book.Authors, cancellationToken, book.SeriesNumber);
        }

        public async Task<SuggestionRelease> CheckAsync(
            string asin,
            string title,
            IReadOnlyList<string> authors,
            CancellationToken cancellationToken = default,
            string? seriesNumber = null)
        {
            if (string.IsNullOrWhiteSpace(asin))
            {
                throw new ArgumentException("A book is checked by its ASIN.", nameof(asin));
            }

            var query = SuggestionReleaseMatch.QueryFor(title, authors);
            var enabled = await indexers.GetEnabledAsync(isAutomaticSearch: false, cancellationToken);
            var profile = await qualityProfiles.GetDefaultAsync();

            // abook.link first: a post that names the book's ASIN is the most certain match
            // there is, and when it answers the Newznab indexers' allowances are not spent.
            var ordered = enabled
                .OrderBy(indexer => IsAbookLink(indexer) ? 0 : 1)
                .ThenBy(indexer => indexer.Priority)
                .ToList();

            var asked = new List<string>();
            var answered = new List<string>();
            foreach (var indexer in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                asked.Add(indexer.Name);

                List<IndexerSearchResult> results;
                try
                {
                    results = await search.SearchIndexerResultsAsync(indexer.Id.ToString(), query);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    logger.LogWarning(ex, "Suggestion release check: {Indexer} failed for {Query}", indexer.Name, query);
                    continue;
                }

                // Only an indexer that answered can be evidence of absence.
                answered.Add(indexer.Name);

                var matching = results
                    .Where(result => IsAbookLink(indexer)
                        ? SuggestionReleaseMatch.AbookPostMatches(
                            result.Asin, result.Album, result.Artist, asin, title, authors,
                            result.SeriesName, result.SeriesPosition, seriesNumber)
                        : SuggestionReleaseMatch.TitleAndAuthorMatch(result.Title, title, authors))
                    .ToList();

                var best = await BestAsync(matching, profile);
                if (best != null)
                {
                    return await SaveAsync(new SuggestionRelease
                    {
                        Asin = asin,
                        Status = SuggestionReleaseStatus.Found,
                        IndexerName = indexer.Name,
                        ReleaseTitle = best.Title,
                        SizeBytes = best.Size > 0 ? best.Size : null,
                        ProtectedResult = secrets.Protect(JsonSerializer.Serialize(best)),
                        Detail = !IsAbookLink(indexer)
                            ? "Title and author match."
                            : string.IsNullOrWhiteSpace(best.Asin) ? "The post's title and author match." : "The post names this ASIN.",
                        CheckedAt = DateTime.UtcNow
                    }, cancellationToken);
                }
            }

            // "Nobody has it" is a claim about the indexers' catalogs, so it may only be
            // made by an indexer that actually answered. When none did, the honest answer
            // is that nothing was learned - see SuggestionReleaseStatus.Unknown.
            var learnedNothing = asked.Count > 0 && answered.Count == 0;
            return await SaveAsync(new SuggestionRelease
            {
                Asin = asin,
                Status = learnedNothing ? SuggestionReleaseStatus.Unknown : SuggestionReleaseStatus.NotFound,
                Detail = asked.Count == 0
                    ? "No indexer is enabled."
                    : learnedNothing
                        ? $"No indexer could be reached: {string.Join(", ", asked)} all failed."
                        : $"No confident match from {string.Join(", ", answered)} for \"{query}\".",
                CheckedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        public async Task<bool> GrabStoredAsync(Audiobook audiobook, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audiobook);
            if (string.IsNullOrWhiteSpace(audiobook.Asin))
            {
                return false;
            }

            var stored = await store.GetAsync(audiobook.Asin.Trim(), cancellationToken);
            if (stored is not { Status: SuggestionReleaseStatus.Found, ProtectedResult: { Length: > 0 } protectedResult })
            {
                return false;
            }

            var result = JsonSerializer.Deserialize<IndexerSearchResult>(secrets.Unprotect(protectedResult))
                ?? throw new InvalidOperationException("The stored release could not be read.");

            // Prepared ahead of time: the NZB is already in hand, so this is a send and
            // nothing more. The download path still resolves the forum reference itself
            // when nothing was prepared, which is what used to happen every time.
            if (stored.Preparation == ReleasePreparationState.Ready
                && stored.ProtectedNzbUrl is { Length: > 0 } preparedUrl)
            {
                result.NzbUrl = secrets.Unprotect(preparedUrl);
            }

            var candidate = TrustedDownloadCandidateFactory.Create(SearchResultConverters.ToSearchResult(result));

            var downloadId = await downloads.SendToDownloadClientAsync(candidate, null, audiobook.Id);
            logger.LogInformation(
                "Grabbed the release found for suggested book '{Title}' ({Asin}) from {Indexer}: {Release} -> download {DownloadId}",
                audiobook.Title, audiobook.Asin, stored.IndexerName, stored.ReleaseTitle, downloadId);
            return !string.IsNullOrEmpty(downloadId);
        }

        /// <summary>
        /// The match to keep: the highest scoring one the default quality profile does not
        /// reject, or with no profile the largest, which for a book is the complete one.
        /// </summary>
        private async Task<IndexerSearchResult?> BestAsync(List<IndexerSearchResult> matching, QualityProfile? profile)
        {
            if (matching.Count == 0)
            {
                return null;
            }

            if (profile == null)
            {
                return matching.OrderByDescending(result => result.Size).First();
            }

            IndexerSearchResult? best = null;
            var bestScore = int.MinValue;
            foreach (var result in matching)
            {
                var score = await qualityProfiles.ScoreSearchResult(SearchResultConverters.ToSearchResult(result), profile);
                if (!score.IsRejected && score.TotalScore > bestScore)
                {
                    best = result;
                    bestScore = score.TotalScore;
                }
            }

            return best;
        }

        private async Task<SuggestionRelease> SaveAsync(SuggestionRelease release, CancellationToken cancellationToken)
        {
            await store.SaveAsync(release, cancellationToken);
            return release;
        }

        private static bool IsAbookLink(Indexer indexer) =>
            string.Equals(indexer.Implementation, AbookLinkImplementation, StringComparison.OrdinalIgnoreCase);
    }
}
