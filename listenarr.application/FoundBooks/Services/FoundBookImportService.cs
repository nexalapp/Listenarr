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

namespace Listenarr.Application.FoundBooks.Services
{
    /// <summary>
    /// A person's Add: resolve the chosen catalogue record and the root, then run the
    /// same import sequence the automatic add uses, in one server-side call.
    /// </summary>
    public sealed class FoundBookImportService(
        IFoundBookRepository repository,
        IRootFolderRepository rootFolderRepository,
        ISearchService searchService,
        MetadataStrategyCoordinator metadataCoordinator,
        IConfigurationService configurationService,
        IFoundBookImportRunner runner) : IFoundBookImportService
    {
        public async Task<FoundBookImportResult> ImportAsync(int id, FoundBookManualImportRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var row = await repository.GetAsync(id, cancellationToken);
            if (row == null)
            {
                return FoundBookImportResult.Fail(FoundBookImportFailure.NotFound, "No such found book.");
            }

            var asin = FirstNonBlank(request.Asin, row.MatchAsin);
            if (asin == null)
            {
                return FoundBookImportResult.Fail(FoundBookImportFailure.NoMatch, "Choose a catalogue match for this book first.", row);
            }

            var rootPath = FirstNonBlank(request.RootPath, (await rootFolderRepository.GetDefaultAsync())?.Path)
                ?? (await rootFolderRepository.GetAllAsync()).FirstOrDefault()?.Path;
            if (rootPath == null)
            {
                return FoundBookImportResult.Fail(FoundBookImportFailure.AddRefused, "No library root folder is configured.", row);
            }

            var sources = await searchService.GetEnabledMetadataSourcesAsync();
            if (sources.Count == 0)
            {
                return FoundBookImportResult.Fail(FoundBookImportFailure.NoMatch, "No metadata source is enabled.", row);
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            var (metadata, _) = await metadataCoordinator.FetchMetadataAsync(asin, sources, null, settings.DefaultSearchRegion);
            if (metadata == null)
            {
                return FoundBookImportResult.Fail(FoundBookImportFailure.NoMatch, $"The catalogue has no record for {asin}.", row);
            }

            // A pack's loose files share one directory; the companion pass would sweep
            // the neighbours' covers and notes into this book, so it stays off for those.
            var shared = FoundBookFolderSharing.SharedRows(await repository.GetAllAsync(cancellationToken));

            return await runner.RunAsync(row, metadata, new FoundBookImportOptions(
                RootPath: rootPath,
                Monitored: request.Monitored,
                AllowDuplicateEdition: request.SeparateBook,
                IncludeCompanions: !shared.Contains(row.Id),
                AutoAdded: false), cancellationToken);
        }

        private static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
    }
}
