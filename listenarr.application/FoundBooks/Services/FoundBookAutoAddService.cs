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
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.FoundBooks.Services
{
    /// <summary>
    /// The automatic add. For every row that is offered, complete and new, ask the
    /// catalogue; when the answer is beyond doubt, add the record, move the files
    /// through the same import a person's Add uses, and finish the row as auto-added.
    /// Anything less certain is left for the Found tab, with the reason in the log.
    /// </summary>
    public sealed class FoundBookAutoAddService(
        IFoundBookRepository repository,
        IConfigurationService configurationService,
        IFoundBookCatalogueMatcher matcher,
        ILibraryAddService libraryAddService,
        IFoundBookImporter importer,
        IFoundBookDecisionService decisions,
        IRootFolderRepository rootFolderRepository,
        ILogger<FoundBookAutoAddService> logger) : IFoundBookAutoAddService
    {
        public const string HistorySource = "FoundBooks";

        public async Task<FoundBookAutoAddSummary> RunAsync(CancellationToken cancellationToken = default)
        {
            var notes = new List<string>();
            var settings = await configurationService.GetApplicationSettingsAsync();
            if (!settings.FoundBooksAutoAdd)
            {
                return new FoundBookAutoAddSummary(0, 0, 0, notes);
            }

            if (!importer.IsAvailable)
            {
                notes.Add("Automatic add is on but no importer is available in this host.");
                logger.LogWarning("{Note}", notes[^1]);
                return new FoundBookAutoAddSummary(0, 0, 0, notes);
            }

            var root = await rootFolderRepository.GetDefaultAsync()
                ?? (await rootFolderRepository.GetAllAsync()).FirstOrDefault();
            if (root == null)
            {
                notes.Add("Automatic add is on but no library root folder is configured.");
                logger.LogWarning("{Note}", notes[^1]);
                return new FoundBookAutoAddSummary(0, 0, 0, notes);
            }

            var rows = await repository.GetAllAsync(cancellationToken);
            var shared = FoundBookFolderSharing.SharedRows(rows);
            var candidates = rows
                .Where(r => r.State == FoundBookState.Pending
                    && r.Completeness == FoundBookCompleteness.Complete
                    && r.LibraryStatus == FoundBookLibraryStatus.New)
                .ToList();

            var added = 0;
            var skipped = 0;
            foreach (var row in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await TryAddAsync(row, root.Path, includeCompanions: !shared.Contains(row.Id), notes, cancellationToken))
                    {
                        added++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && WorkerExceptionClassifier.IsNonFatal(ex))
                {
                    skipped++;
                    notes.Add($"{Describe(row)}: {ex.Message}");
                    logger.LogError(ex, "Automatic add failed for found book {Id}", row.Id);
                    await decisions.AbortImportAsync(row.Id, cancellationToken);
                }
            }

            return new FoundBookAutoAddSummary(candidates.Count, added, skipped, notes);
        }

        private async Task<bool> TryAddAsync(
            FoundBook row,
            string rootPath,
            bool includeCompanions,
            List<string> notes,
            CancellationToken cancellationToken)
        {
            var match = await matcher.MatchAsync(row, cancellationToken);
            if (match == null || !match.HighConfidence)
            {
                notes.Add($"{Describe(row)}: {(match == null ? "no catalogue match" : match.Reason)} — left for review.");
                logger.LogInformation("Found book {Id} not auto-added: {Reason}", row.Id, notes[^1]);
                return false;
            }

            var begun = await decisions.BeginImportAsync(row.Id, cancellationToken);
            if (!begun.Success)
            {
                notes.Add($"{Describe(row)}: {begun.Error}");
                return false;
            }

            var add = await libraryAddService.AddToLibraryAsync(new LibraryAddOperationRequest
            {
                Metadata = match.Metadata,
                Monitored = true,
                DestinationPath = rootPath,
                HistorySource = HistorySource
            }, cancellationToken);

            var audiobook = add.Audiobook;
            if (audiobook == null || (!add.Added && !add.AlreadyExists))
            {
                await decisions.AbortImportAsync(row.Id, cancellationToken);
                notes.Add($"{Describe(row)}: could not add the record ({add.ValidationMessage ?? add.Message}).");
                return false;
            }

            if (add.AlreadyExists && (!string.IsNullOrWhiteSpace(audiobook.FilePath) || (audiobook.Files?.Count ?? 0) > 0))
            {
                // The library already has this edition with a file. The scan's own
                // library match missed it (a different spelling, most likely); a person
                // should decide whether this is a second copy.
                await decisions.AbortImportAsync(row.Id, cancellationToken);
                notes.Add($"{Describe(row)}: the library already holds {match.Metadata.Asin}; left for review.");
                return false;
            }

            var import = await importer.ImportAsync(row, audiobook.Id, includeCompanions, cancellationToken);
            if (!import.Success)
            {
                await decisions.AbortImportAsync(row.Id, cancellationToken);
                notes.Add($"{Describe(row)}: import failed ({import.Error ?? $"{import.ImportedCount} of {import.TotalCount} files"}).");
                logger.LogWarning("Automatic add of found book {Id} into audiobook {AudiobookId} failed: {Error}", row.Id, audiobook.Id, import.Error);
                return false;
            }

            var finished = await decisions.FinishImportAsync(row.Id, audiobook.Id, autoAdded: true, cancellationToken);
            if (!finished.Success)
            {
                notes.Add($"{Describe(row)}: {finished.Error}");
                return false;
            }

            logger.LogInformation("Automatically added found book {Id} as audiobook {AudiobookId} ({Reason})", row.Id, audiobook.Id, match.Reason);
            return true;
        }

        private static string Describe(FoundBook row) =>
            string.IsNullOrWhiteSpace(row.DetectedTitle) ? row.BookFolder : row.DetectedTitle;
    }
}
