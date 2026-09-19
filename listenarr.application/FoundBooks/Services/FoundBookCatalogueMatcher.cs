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
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Application.Search.Strategies;
using Listenarr.Domain.Common;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.FoundBooks.Services
{
    public sealed class FoundBookCatalogueMatcher(
        ISearchService searchService,
        MetadataStrategyCoordinator metadataCoordinator,
        IConfigurationService configurationService,
        ILogger<FoundBookCatalogueMatcher> logger) : IFoundBookCatalogueMatcher
    {
        private const int CandidateLimit = 10;

        public async Task<FoundBookCatalogueMatch?> MatchAsync(FoundBook row, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(row);
            var sources = await searchService.GetEnabledMetadataSourcesAsync();
            if (sources.Count == 0)
            {
                logger.LogDebug("No metadata sources are enabled; cannot match found book {Id}", row.Id);
                return null;
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            var region = settings.DefaultSearchRegion;

            if (!string.IsNullOrWhiteSpace(row.DetectedAsin))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (byAsin, _) = await metadataCoordinator.FetchMetadataAsync(row.DetectedAsin, sources, null, region);
                if (byAsin != null)
                {
                    return new FoundBookCatalogueMatch(byAsin, true, $"ASIN {row.DetectedAsin} from the files' tags.");
                }
            }

            var titleKey = FoundBookLibraryMatcher.TitleKey(row.DetectedTitle);
            if (titleKey.Length == 0)
            {
                return null;
            }

            var query = string.IsNullOrWhiteSpace(row.DetectedAuthor)
                ? $"TITLE:{row.DetectedTitle}"
                : $"AUTHOR:{row.DetectedAuthor} TITLE:{row.DetectedTitle}";
            var results = await searchService.IntelligentSearchAsync(
                query,
                candidateLimit: CandidateLimit,
                returnLimit: CandidateLimit,
                region: region,
                ct: cancellationToken);

            var authorKey = FileUtils.NormalizeComparisonValue(row.DetectedAuthor);
            var exact = results.FirstOrDefault(r =>
                string.Equals(FoundBookLibraryMatcher.TitleKey(r.Title), titleKey, StringComparison.Ordinal)
                && (authorKey.Length == 0
                    || FoundBookLibraryMatcher.AuthorsAgree(FileUtils.NormalizeComparisonValue(r.Author), authorKey)));
            var candidate = exact ?? results.FirstOrDefault();
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Asin))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var (metadata, _) = await metadataCoordinator.FetchMetadataAsync(candidate.Asin, sources, null, region);
            if (metadata == null)
            {
                return null;
            }

            // A title-only exact match with no author on either side is not beyond doubt:
            // there is more than one "Us".
            var high = exact != null && authorKey.Length > 0;
            return new FoundBookCatalogueMatch(
                metadata,
                high,
                high
                    ? $"Title and author agree with {candidate.Asin}."
                    : $"Nearest catalogue result is {candidate.Asin}; not exact.");
        }
    }
}
