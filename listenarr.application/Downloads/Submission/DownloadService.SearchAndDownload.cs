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

using Listenarr.Application.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission
{
    /// <summary>
    /// The automatic path: find a release for a book already in the library and send the
    /// best one to a client.
    ///
    /// Split out because it is a whole operation of its own - search, score, pick, submit -
    /// and keeping it beside the manual submission path pushed the file past the size
    /// BackendArchitectureTests enforces.
    /// </summary>
    public partial class DownloadService
    {
        public async Task<SearchAndDownloadResult> SearchAndDownloadAsync(int audiobookId)
        {
            // Get the audiobook
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new SearchAndDownloadResult
                {
                    Success = false,
                    Message = "Audiobook not found"
                };
            }

            if (audiobook.QualityProfile == null)
            {
                logger.LogWarning("Audiobook '{Title}' has no quality profile assigned", audiobook.Title);
                return new SearchAndDownloadResult
                {
                    Success = false,
                    Message = "Audiobook has no quality profile assigned"
                };
            }

            // Build search query from audiobook metadata
            var searchQuery = DownloadSearchQueryBuilder.Build(audiobook);
            logger.LogInformation("Searching for audiobook '{Title}' with query: {Query}", LogRedaction.SanitizeText(audiobook.Title), LogRedaction.SanitizeText(searchQuery));

            // Search using the working search service. This is an automatic search (triggered
            // by the background/manual 'search-and-download' endpoint), so set isAutomaticSearch
            // to true to ensure only indexers are queried (no Amazon/Audible scraping).
            var searchResults = await searchService.SearchAsync(searchQuery, isAutomaticSearch: true);

            if (searchResults == null || !searchResults.Any())
            {
                return new SearchAndDownloadResult
                {
                    Success = false,
                    NothingObtainable = true,
                    Message = "No search results found"
                };
            }

            // Score results against quality profile
            var scoredResults = await qualityProfileService.ScoreSearchResults(searchResults, audiobook.QualityProfile);

            // Log all scored results for debugging
            logger.LogInformation("Scored {Count} search results for audiobook '{Title}':", scoredResults.Count, LogRedaction.SanitizeText(audiobook.Title));
            foreach (var scoredResult in scoredResults.OrderByDescending(s => s.TotalScore))
            {
                var status = scoredResult.IsRejected ? "REJECTED" : (scoredResult.TotalScore > 0 ? "ACCEPTABLE" : "LOW SCORE");
                logger.LogInformation("  [{Status}] Score: {Score} | Title: {Title} | Source: {Source} | Size: {Size}MB | Seeders: {Seeders} | Quality: {Quality}",
                    status, scoredResult.TotalScore, LogRedaction.SanitizeText(scoredResult.SearchResult.Title), LogRedaction.SanitizeText(scoredResult.SearchResult.Source),
                    scoredResult.SearchResult.Size / 1024 / 1024, scoredResult.SearchResult.Seeders, scoredResult.SearchResult.Quality);
                if (scoredResult.IsRejected && scoredResult.RejectionReasons.Any())
                {
                    logger.LogInformation("    Rejection reasons: {Reasons}", string.Join(", ", scoredResult.RejectionReasons));
                }
            }

            // Only consider non-rejected, score > 0 results
            var acceptable = scoredResults
                .Where(s => !s.IsRejected && s.TotalScore > 0)
                .OrderByDescending(s => s.TotalScore)
                .ToList();

            // Releases that already failed for this book are out, however well they
            // score. Without this the search is deterministic in the worst way: the same
            // ranking picks the release that just failed, it fails again, and the failure
            // starts the search once more. The whole point of searching again after a
            // failure is to reach a different release.
            var blocklist = new ReleaseBlocklist(
                await releaseBlocklistRepository.GetForAudiobookAsync(audiobookId));
            var sendable = acceptable
                .Where(scored => !blocklist.IsBlocked(
                    scored.SearchResult.Title,
                    scored.SearchResult.Id,
                    scored.SearchResult.Size))
                .ToList();

            if (sendable.Count == 0)
            {
                var blocked = acceptable.Count;
                if (blocked > 0 && blocklist.Count > 0)
                {
                    // Every release this book has is one that already failed. Saying so
                    // is the difference between "nothing holds it" and "everything that
                    // holds it is broken", and only the second is worth a person's time.
                    logger.LogInformation(
                        "Every one of the {Count} acceptable release(s) for audiobook '{Title}' is blocked after an earlier failure; nothing left to grab",
                        blocked,
                        LogRedaction.SanitizeText(audiobook.Title));
                    return new SearchAndDownloadResult
                    {
                        Success = false,
                        NothingObtainable = true,
                        Message = blocked == 1
                            ? "The only release found for this book already failed"
                            : $"All {blocked} releases found for this book have already failed"
                    };
                }

                logger.LogWarning("No acceptable search results found for audiobook '{Title}' after quality filtering", audiobook.Title);
                return new SearchAndDownloadResult
                {
                    Success = false,
                    NothingObtainable = true,
                    Message = "No acceptable search results found"
                };
            }

            // Down the ranking until one can actually be fetched, rather than giving up
            // on the first. A release an indexer lists is not a release that can be had:
            // an abook.link topic whose payload no index holds throws here, and when that
            // was the end of the road the book kept a failed row nobody could clear - the
            // search had found something, so "nothing obtainable" was false, and the grab
            // had thrown, so nothing was sent either. Measured on 'Disturb' (4904).
            DownloadClientSubmissionException? lastRefusal = null;
            foreach (var scored in sendable)
            {
                scored.SearchResult.Score = scored.TotalScore;

                var candidate = TrustedDownloadCandidateFactory.Create(scored.SearchResult);
                var isTorrent = candidate.SourceDescriptor.Protocol == DownloadProtocol.Torrent;
                var downloadClientId = await downloadClientSelector.GetAppropriateDownloadClientAsync(isTorrent);

                if (downloadClientId == null)
                {
                    // The client is this installation's, not this release's, so there is
                    // nothing to be learned by trying the next one.
                    logger.LogWarning("No suitable download client found for type: {Type}", isTorrent ? "Torrent" : "NZB");
                    return new SearchAndDownloadResult
                    {
                        Success = false,
                        Message = $"No suitable download client found for {(isTorrent ? "torrent" : "NZB")} results"
                    };
                }

                string downloadId2;
                try
                {
                    downloadId2 = await SendToDownloadClientAsync(
                        candidate,
                        downloadClientId,
                        audiobookId,
                        allowBlockedRelease: false,
                        automatic: true);
                }
                catch (DownloadClientSubmissionException refusal)
                {
                    lastRefusal = refusal;
                    logger.LogWarning(
                        "'{Release}' could not be fetched for audiobook '{Title}': {Reason}. Trying the next release.",
                        LogRedaction.SanitizeText(scored.SearchResult.Title),
                        LogRedaction.SanitizeText(audiobook.Title),
                        refusal.Message);
                    continue;
                }

                // An empty id is the blocklist refusing at submission, which is a release
                // this book has already lost rather than a reason to stop.
                if (string.IsNullOrEmpty(downloadId2))
                {
                    continue;
                }

                await LogDownloadHistory(audiobook, "Search", scored.SearchResult);

                return new SearchAndDownloadResult
                {
                    Success = true,
                    Message = $"Successfully sent to download client",
                    DownloadId = downloadId2,
                    IndexerUsed = "Search",
                    DownloadClientUsed = downloadClientId,
                    SearchResult = scored.SearchResult
                };
            }

            logger.LogWarning(
                "None of the {Count} release(s) found for audiobook '{Title}' could be fetched",
                sendable.Count,
                LogRedaction.SanitizeText(audiobook.Title));

            return new SearchAndDownloadResult
            {
                Success = false,
                // Listed everywhere and fetchable nowhere is, for now, the same as nobody
                // holding it: there is nothing further to try, and the book has to say so
                // rather than keep a failed row that no later run will clear. The stored
                // answer comes due again, so this is a statement about today.
                NothingObtainable = true,
                Message = lastRefusal == null
                    ? "No release found for this book could be fetched"
                    : $"No release found for this book could be fetched: {lastRefusal.Message}"
            };
        }
    }
}
