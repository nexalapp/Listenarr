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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Downloads.Submission
{
    /// <summary>
    /// Turns an abook.link search result into something a download client will take.
    ///
    /// abook.link results carry a topic reference rather than an NZB link, because the
    /// payload that yields one is behind a publicly visible "thanks". Resolution therefore
    /// happens here, at submission, when a grab has actually been asked for — the same
    /// reason MyAnonamouse resolves its torrents at this point rather than at search.
    ///
    /// This is also where the archive password joins the submission. It is read from the
    /// same post as the search string and would otherwise be lost between the two.
    /// </summary>
    public sealed class AbookLinkSourceResolver : IDownloadSourceResolver
    {
        /// <summary>How abook.link search results identify themselves.</summary>
        internal const string IdPrefix = "abook:";

        private readonly IAbookGrabResolver _grabs;
        private readonly INzbFileDownloader _downloader;
        private readonly ILogger<AbookLinkSourceResolver> _logger;

        public AbookLinkSourceResolver(
            IAbookGrabResolver grabs,
            INzbFileDownloader downloader,
            ILogger<AbookLinkSourceResolver> logger)
        {
            _grabs = grabs ?? throw new ArgumentNullException(nameof(grabs));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Resolvers are tried highest first, so this sits above the generic usenet
        /// resolver, which would reject these results for carrying no NZB locator.
        /// </summary>
        public int Priority => 90;

        /// <summary>
        /// Claims a candidate either by its indexer implementation or by the prefix on its
        /// release id.
        ///
        /// The id prefix is the reliable half: the implementation name has to survive a
        /// round trip through a search response and an encrypted download reference, and
        /// when it does not the generic usenet resolver picks the candidate up and rejects
        /// it for having no NZB locator - which is what happened the first time a result
        /// was grabbed from the interactive list.
        /// </summary>
        public bool CanResolve(TrustedDownloadCandidate candidate)
            => string.Equals(
                   candidate.SourceDescriptor.IndexerImplementation,
                   "AbookLink",
                   StringComparison.OrdinalIgnoreCase)
               || TryReadTopicId(candidate) is not null;

        public async Task<PreparedDownloadSubmission> ResolveAsync(
            TrustedDownloadCandidate candidate,
            string? provisionalDownloadId,
            CancellationToken cancellationToken)
        {
            if (TryReadTopicId(candidate) is not { } topicId)
            {
                throw new DownloadClientSubmissionException(
                    "This abook.link result carries no topic reference, so there is nothing to grab.");
            }

            _logger.LogInformation("Resolving abook.link topic {TopicId} for '{Title}'", topicId, candidate.Title);

            var grab = await _grabs.ResolveAsync(topicId, cancellationToken);

            if (!grab.Succeeded || grab.NzbUrl is not { Length: > 0 })
            {
                // Carry the grab's own words through: it knows whether the post would not
                // open, no index held the release, or it is merely still propagating, and
                // a generic failure here would throw all of that away.
                throw new DownloadClientSubmissionException(
                    $"{grab.Stage}: {grab.Detail}");
            }

            var bytes = await _downloader.DownloadAsync(grab.NzbUrl, null, cancellationToken);
            if (bytes.Length == 0)
            {
                throw new DownloadClientSubmissionException("The index returned an empty NZB.");
            }

            return new PreparedUsenetSubmission(
                candidate.Title,
                candidate.Artist,
                candidate.Album,
                candidate.Source,
                candidate.Quality,
                candidate.Language,
                candidate.Size,
                grab.NzbUrl,
                bytes,
                $"{SanitizeFileName(candidate.Title)}.nzb",
                grab.Password);
        }

        /// <summary>
        /// Reads the topic id from the candidate. Search results are identified as
        /// <c>abook:&lt;topicId&gt;</c>, which is what makes them recognisable here without
        /// relying on any other field.
        /// </summary>
        internal static int? TryReadTopicId(TrustedDownloadCandidate candidate)
        {
            foreach (var value in candidate.SourceDescriptor.Locators
                         .Where(locator => locator.Kind == DownloadSourceLocatorKind.ReleaseId)
                         .Select(locator => locator.Value)
                         .Append(candidate.Id))
            {
                if (value is not { Length: > 0 })
                {
                    continue;
                }

                var text = value.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase)
                    ? value[IdPrefix.Length..]
                    : null;

                if (text is not null && int.TryParse(text, out var topicId))
                {
                    return topicId;
                }
            }

            return null;
        }

        private static string SanitizeFileName(string value)
        {
            var cleaned = new string(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' or ' ' ? c : '_').ToArray());
            return cleaned.Trim();
        }
    }
}
