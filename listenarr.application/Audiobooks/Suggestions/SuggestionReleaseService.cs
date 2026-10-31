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
            string? seriesNumber = null,
            int? runtimeMinutes = null);

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
        IAbookLinkBrowser abook,
        ILogger<SuggestionReleaseService> logger) : ISuggestionReleaseService
    {
        private const string AbookLinkImplementation = "AbookLink";

        /// <summary>
        /// How many of a check's matches are read in full. A search narrowed to the one
        /// book being asked about rarely answers with more than two or three posts of it,
        /// and a library of a thousand books is worth one request each, not sixteen.
        /// </summary>
        private const int MaxPostsReadPerCheck = 3;

        public Task<SuggestionRelease> CheckAsync(SuggestedBook book, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(book);
            if (string.IsNullOrWhiteSpace(book.Asin))
            {
                throw new ArgumentException("A suggested book is checked by its ASIN.", nameof(book));
            }

            return CheckAsync(
                book.Asin, book.Title, book.Authors, cancellationToken, book.SeriesNumber, book.Runtime);
        }

        public async Task<SuggestionRelease> CheckAsync(
            string asin,
            string title,
            IReadOnlyList<string> authors,
            CancellationToken cancellationToken = default,
            string? seriesNumber = null,
            int? runtimeMinutes = null)
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
                    results = await search.SearchIndexerResultsAsync(
                        indexer.Id.ToString(), query, null, AvailabilityRequest(query));
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    logger.LogWarning(ex, "Suggestion release check: {Indexer} failed for {Query}", indexer.Name, query);
                    continue;
                }

                // Only an indexer that answered can be evidence of absence.
                answered.Add(indexer.Name);

                var plausible = results
                    .Where(result => SuggestionReleaseMatch.CouldBeTheRecording(result.Size, runtimeMinutes))
                    .ToList();

                var matching = IsAbookLink(indexer)
                    ? await ConfirmAbookMatchesAsync(
                        AbookShortlist(plausible, asin, title, authors, seriesNumber),
                        asin, title, authors, seriesNumber, runtimeMinutes, cancellationToken)
                    : [.. plausible.Where(result =>
                        SuggestionReleaseMatch.TitleAndAuthorMatch(
                            result.Title, title, authors, seriesNumber))];

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
                // Returning a bare false here is how a book came to be added, monitored
                // and silently never downloaded: the caller discarded the answer and the
                // run reported itself clean.
                logger.LogInformation(
                    "No stored release to grab for '{Title}' ({Asin}): {Reason}",
                    audiobook.Title,
                    audiobook.Asin,
                    stored is null ? "it has never been checked"
                        : stored.Status != SuggestionReleaseStatus.Found
                            ? $"its last check said {stored.Status}"
                            : "the check found one but kept no payload");
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
            if (string.IsNullOrEmpty(downloadId))
            {
                logger.LogWarning(
                    "The download client accepted nothing for '{Title}' ({Asin}) from {Indexer}: {Release}",
                    audiobook.Title, audiobook.Asin, stored.IndexerName, stored.ReleaseTitle);
                return false;
            }

            logger.LogInformation(
                "Grabbed the release found for suggested book '{Title}' ({Asin}) from {Indexer}: {Release} -> download {DownloadId}",
                audiobook.Title, audiobook.Asin, stored.IndexerName, stored.ReleaseTitle, downloadId);
            return true;
        }

        /// <summary>
        /// What the check asks a source for: the posts, not what is inside them. abook.link
        /// reads this as permission to judge every hit from its topic title and open none,
        /// which turns a check from sixteen requests to its forum into one.
        /// </summary>
        private static SearchRequest AvailabilityRequest(string query) => new()
        {
            Query = query,
            IncludeEnrichment = false
        };

        /// <summary>
        /// The posts worth reading, best first. A post is a candidate on its parsed fields
        /// or, failing that, on its title line carrying the book's title and its author's
        /// surname - which is the looser rule, and is allowed here because a shortlist is
        /// not a verdict: every one of these is read and judged again before it counts.
        /// A post that already declares an ASIN is exempt: it has said which edition it
        /// is, and its title line cannot argue with that.
        ///
        /// It is needed because the title line is not always the "Author - Series NN -
        /// Title (Year)" the parser is built for. "SERRAted Edge 10 - Breaking Silence by
        /// Mercedes Lackey, Cody Martin" parses to an author of "SERRAted Edge 10", so the
        /// book is not recognised in any field - but both its title and its author are
        /// plainly there in the line. Such a post used to be rescued by being opened, back
        /// when the first fifteen hits always were.
        /// </summary>
        private static List<IndexerSearchResult> AbookShortlist(
            List<IndexerSearchResult> results,
            string asin,
            string title,
            IReadOnlyList<string> authors,
            string? seriesNumber)
        {
            bool ParsedFieldsMatch(IndexerSearchResult result) =>
                SuggestionReleaseMatch.AbookPostMatchesAllowingBranding(
                    result.Asin, result.Album, result.Artist, result.SeriesName,
                    result.SeriesPosition, asin, title, authors, seriesNumber);

            return
            [
                .. results.Where(ParsedFieldsMatch),
                .. results.Where(result => !ParsedFieldsMatch(result)
                    && string.IsNullOrWhiteSpace(result.Asin)
                    && SuggestionReleaseMatch.TitleAndAuthorMatch(result.Title, title, authors))
            ];
        }

        /// <summary>
        /// Reads the posts behind the shortlist and judges them again on what the post
        /// says. The titles can only narrow: an ASIN lives in the post, and a post that
        /// declares one is held to it, so a title that looks right is a candidate and
        /// nothing more until the post has been read. Reading the shortlist rather than
        /// the search means two or three topics opened per check instead of fifteen, and
        /// none at all for a book nothing on the forum is even titled after.
        ///
        /// A post that could not be read falls back to the fields the search itself gave,
        /// which is the same answer a hit past the old inspection limit always got.
        /// </summary>
        private async Task<List<IndexerSearchResult>> ConfirmAbookMatchesAsync(
            List<IndexerSearchResult> shortlist,
            string asin,
            string title,
            IReadOnlyList<string> authors,
            string? seriesNumber,
            int? runtimeMinutes,
            CancellationToken cancellationToken)
        {
            var confirmed = new List<IndexerSearchResult>();

            // A post nobody could read is judged on the fields the search gave, which is
            // the answer a hit past the old inspection limit always got. A hit that was
            // only shortlisted because its title line reads right does not survive that.
            void KeepIfAlreadyAMatch(IndexerSearchResult result)
            {
                if (SuggestionReleaseMatch.AbookPostMatchesAllowingBranding(
                    result.Asin, result.Album, result.Artist, result.SeriesName,
                    result.SeriesPosition, asin, title, authors, seriesNumber))
                {
                    confirmed.Add(result);
                }
            }

            foreach (var result in shortlist.Take(MaxPostsReadPerCheck))
            {
                if (!int.TryParse(result.DownloadReference, out var topicId))
                {
                    KeepIfAlreadyAMatch(result);
                    continue;
                }

                AbookBrowseResult topic;
                try
                {
                    topic = await abook.GetTopicAsync(topicId, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    logger.LogWarning(ex, "Suggestion release check: could not read abook.link topic {TopicId}", topicId);
                    KeepIfAlreadyAMatch(result);
                    continue;
                }

                if (!topic.Succeeded || topic.Candidates.Count == 0)
                {
                    KeepIfAlreadyAMatch(result);
                    continue;
                }

                var post = topic.Candidates[0].Post;
                result.Size = post.SizeBytes ?? result.Size;
                result.Files = post.FileCount ?? result.Files;
                result.Format = post.Format ?? result.Format;
                result.Quality = post.Format ?? result.Quality;
                result.Narrator = post.Narrator ?? result.Narrator;
                result.Publisher = post.Publisher ?? result.Publisher;
                result.Asin = post.Asin ?? result.Asin;
                result.SeriesName = post.SeriesName ?? result.SeriesName;
                result.SeriesPosition = post.SeriesPosition ?? result.SeriesPosition;
                result.Album = post.Title ?? result.Album;
                result.Artist = post.Author ?? result.Artist;

                // The size is one of the things the post was read for, so a release too
                // small to be the recording is only visible now.
                if (SuggestionReleaseMatch.AbookPostMatchesAllowingBranding(
                        result.Asin, result.Album, result.Artist, result.SeriesName,
                        result.SeriesPosition, asin, title, authors, seriesNumber)
                    && SuggestionReleaseMatch.CouldBeTheRecording(result.Size, runtimeMinutes))
                {
                    confirmed.Add(result);
                }
            }

            return confirmed;
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
