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

using Listenarr.Domain.Common;

namespace Listenarr.Application.Search.Core
{
    public partial class SearchService : ISearchService
    {

        /// <summary>
        /// Other recordings of the same book, from a catalogue that names its readers.
        /// Never throws and never blocks a search: a shop's answer with no alternates is
        /// better than no answer at all.
        /// </summary>
        private async Task<IReadOnlyList<MetadataSearchResult>> AlternateEditionsAsync(
            string? title,
            string? author,
            CancellationToken ct)
        {
            if (_overDriveService == null || string.IsNullOrWhiteSpace(title))
            {
                return [];
            }

            try
            {
                var editions = await _overDriveService.SearchAsync(title, author, ct);
                return editions.Select(edition => new MetadataSearchResult
                {
                    Id = $"overdrive:{edition.Id}",
                    Title = TitleCleanup.StripNarratorSuffix(TitleCasing.ToTitleCase(edition.Title)) ?? edition.Title,
                    Artist = string.Join(", ", edition.Authors),
                    Narrator = string.Join(", ", edition.Narrators),
                    Publisher = edition.Publisher,
                    PublishYear = edition.PublishYear,
                    Runtime = edition.RuntimeMinutes,
                    ImageUrl = edition.ImageUrl,
                    Source = "OverDrive",
                    MetadataSource = "OverDrive",
                    IsEnriched = true
                }).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not ask a library catalogue about {Title}", title);
                return [];
            }
        }

        /// <summary>
        /// A shop's results with any recording it does not sell appended, deduplicated on
        /// title and reader so an edition already listed is not repeated.
        /// </summary>
        private async Task<List<MetadataSearchResult>> WithAlternateEditionsAsync(
            List<MetadataSearchResult> results,
            Task<IReadOnlyList<MetadataSearchResult>> alternatesTask)
        {
            IReadOnlyList<MetadataSearchResult> alternates;
            try
            {
                alternates = await alternatesTask;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Alternate editions were not available");
                return results;
            }

            var added = 0;
            foreach (var alternate in alternates)
            {
                var duplicate = results.Any(existing =>
                    string.Equals(existing.Title?.Trim(), alternate.Title?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && NarratorsOfMetadata(existing).SetEquals(NarratorsOfMetadata(alternate)));
                if (!duplicate)
                {
                    results.Add(alternate);
                    added++;
                }
            }

            if (added > 0)
            {
                _logger.LogInformation("Offered {Added} alternate edition(s) from a library catalogue", added);
            }

            return results;
        }

        private static HashSet<string> NarratorsOfMetadata(MetadataSearchResult result) =>
            (result.Narrator ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
