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

        public async Task<SuggestionRelease> CheckAsync(SuggestedBook book, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(book);
            if (string.IsNullOrWhiteSpace(book.Asin))
            {
                throw new ArgumentException("A suggested book is checked by its ASIN.", nameof(book));
            }

            var query = SuggestionReleaseMatch.QueryFor(book.Title, book.Authors);
            var enabled = await indexers.GetEnabledAsync(isAutomaticSearch: false, cancellationToken);
            var profile = await qualityProfiles.GetDefaultAsync();

            // abook.link first: a post that names the book's ASIN is the most certain match
            // there is, and when it answers the Newznab indexers' allowances are not spent.
            var ordered = enabled
                .OrderBy(indexer => IsAbookLink(indexer) ? 0 : 1)
                .ThenBy(indexer => indexer.Priority)
                .ToList();

            var asked = new List<string>();
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

                var matching = results
                    .Where(result => IsAbookLink(indexer)
                        ? SuggestionReleaseMatch.AbookPostMatches(
                            result.Asin, result.Album, result.Artist, book.Asin, book.Title, book.Authors)
                        : SuggestionReleaseMatch.TitleAndAuthorMatch(result.Title, book.Title, book.Authors))
                    .ToList();

                var best = await BestAsync(matching, profile);
                if (best != null)
                {
                    return await SaveAsync(new SuggestionRelease
                    {
                        Asin = book.Asin,
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

            return await SaveAsync(new SuggestionRelease
            {
                Asin = book.Asin,
                Status = SuggestionReleaseStatus.NotFound,
                Detail = asked.Count == 0
                    ? "No indexer is enabled."
                    : $"No confident match from {string.Join(", ", asked)} for \"{query}\".",
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
