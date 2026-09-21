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
using System.Data.Common;
using Listenarr.Application.Common;
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.FoundBooks.Services
{
    public sealed class FoundBookImportRunner(
        ILibraryAddService libraryAddService,
        ILibraryDestinationPlanner destinationPlanner,
        IFoundBookImporter importer,
        IFoundBookDecisionService decisions,
        ILogger<FoundBookImportRunner> logger) : IFoundBookImportRunner
    {
        public const string HistorySource = "FoundBooks";

        // The abort is what keeps a failed import from stranding the row, and the
        // failure that needs it most is the database being briefly unavailable — the
        // same failure that makes the abort itself fail. So it gets more than one go.
        private static readonly TimeSpan[] AbortRetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)];

        public async Task<FoundBookImportResult> RunAsync(
            FoundBook row,
            AudibleBookMetadata metadata,
            FoundBookImportOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(options);

            if (!importer.IsAvailable)
            {
                return FoundBookImportResult.Fail(FoundBookImportFailure.Unavailable, "No importer is available in this host.");
            }

            // A queued request arrives already importing: the row was marked when it
            // was queued, so a restart in between finds it. Anything else is marked here.
            var queued = row.State == FoundBookState.Importing;
            if (!queued)
            {
                var begun = await decisions.BeginImportAsync(row.Id, null, cancellationToken);
                if (!begun.Success)
                {
                    return FoundBookImportResult.Fail(
                        begun.Failure == FoundBookDecisionFailure.NotFound ? FoundBookImportFailure.NotFound : FoundBookImportFailure.WrongState,
                        begun.Error ?? "The book cannot be imported in its current state.");
                }
            }

            try
            {
                return await ImportBegunAsync(row, metadata, options, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller's own cancellation: the host stopping. A queued row keeps its
                // request so the next start resumes it; a row this call marked has no
                // request to resume and must not stay importing forever. The abort gets
                // a token of its own because the caller's is already signalled.
                if (!queued)
                {
                    await AbortAsync(row.Id, "The import was cancelled before it finished.", CancellationToken.None);
                }

                throw;
            }
            catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex) || ex is OperationCanceledException)
            {
                // An OperationCanceledException the caller did not ask for is a timeout
                // somewhere below - an HttpClient giving up on a cover image, say - and
                // is a failure of this import, not a request to stop.
                logger.LogError(ex, "Import of found book {Id} failed", row.Id);
                var (failure, error) = IsPersistenceFailure(ex)
                    ? (FoundBookImportFailure.Persistence,
                        "The database was unavailable, so the import stopped part-way. It will be tried again, and anything already moved is picked up where it was left.")
                    : ex is OperationCanceledException
                        ? (FoundBookImportFailure.ImportFailed, "A step timed out before it finished.")
                        : (FoundBookImportFailure.ImportFailed, ex.Message);
                var book = await AbortAsync(row.Id, error, cancellationToken);
                return FoundBookImportResult.Fail(failure, error, book);
            }
        }

        private async Task<FoundBookImportResult> ImportBegunAsync(
            FoundBook row,
            AudibleBookMetadata metadata,
            FoundBookImportOptions options,
            CancellationToken cancellationToken)
        {
            var destination = await destinationPlanner.PlanBookFolderAsync(metadata, options.RootPath, cancellationToken);
            var add = await libraryAddService.AddToLibraryAsync(new LibraryAddOperationRequest
            {
                Metadata = metadata,
                Monitored = options.Monitored,
                DestinationPath = destination.FullPath,
                AllowDuplicateEdition = options.AllowDuplicateEdition,
                HistorySource = HistorySource
            }, cancellationToken);

            var audiobook = add.Audiobook;
            if (audiobook == null || (!add.Added && !add.AlreadyExists))
            {
                var error = $"Could not add the record ({add.ValidationMessage ?? add.Message}).";
                return FoundBookImportResult.Fail(FoundBookImportFailure.AddRefused, error, await AbortAsync(row.Id, error, cancellationToken));
            }

            var alreadyHasFile = !string.IsNullOrWhiteSpace(audiobook.FilePath) || (audiobook.Files?.Count ?? 0) > 0;
            if (add.AlreadyExists && alreadyHasFile && options.AutoAdded)
            {
                // The library already has this edition with a file. The scan's own
                // library match missed it (a different spelling, most likely); a person
                // should decide whether this is a second copy. A person's Add is that
                // decision, so it goes on and the file joins the held record.
                var error = $"The library already holds {metadata.Asin}; left for review.";
                return FoundBookImportResult.Fail(FoundBookImportFailure.AddRefused, error, await AbortAsync(row.Id, error, cancellationToken));
            }

            // A record that already exists is the retry case: a previous attempt got as
            // far as adding it, or further. Reusing it is what makes a second Add safe,
            // and the importer asks only for the files a previous attempt did not move.
            var import = await importer.ImportAsync(row, audiobook.Id, options.IncludeCompanions, cancellationToken);
            if (!import.Success)
            {
                var error = import.Error ?? $"{import.ImportedCount} of {import.TotalCount} files imported.";
                logger.LogWarning("Import of found book {Id} into audiobook {AudiobookId} failed: {Error}", row.Id, audiobook.Id, error);
                return FoundBookImportResult.Fail(FoundBookImportFailure.ImportFailed, error, await AbortAsync(row.Id, error, cancellationToken));
            }

            var finished = await decisions.FinishImportAsync(row.Id, audiobook.Id, options.AutoAdded, cancellationToken);
            if (!finished.Success)
            {
                // Finish already put the row back when files remain; any other refusal
                // leaves it importing, which the abort undoes.
                var error = finished.Error ?? "The import could not be finished.";
                var book = finished.Failure == FoundBookDecisionFailure.FilesRemain
                    ? finished.Book
                    : await AbortAsync(row.Id, error, cancellationToken);
                return FoundBookImportResult.Fail(FoundBookImportFailure.FinishFailed, error, book);
            }

            return FoundBookImportResult.Ok(audiobook.Id, finished.Book!, finished.Skipped);
        }

        private async Task<FoundBook?> AbortAsync(int id, string error, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var aborted = await decisions.AbortImportAsync(id, error, cancellationToken);
                    return aborted.Book;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && (WorkerExceptionClassifier.IsNonFatal(ex) || ex is OperationCanceledException))
                {
                    if (attempt >= AbortRetryDelays.Length)
                    {
                        // Startup recovery resets any row still marked importing.
                        logger.LogError(ex, "Could not put found book {Id} back after a failed import; it will be reset on the next start", id);
                        return null;
                    }

                    logger.LogWarning(ex, "Could not put found book {Id} back after a failed import; retrying", id);
                    await Task.Delay(AbortRetryDelays[attempt], cancellationToken);
                }
            }
        }

        /// <summary>
        /// A refusal from the database rather than from the import. The provider's own
        /// exception derives from <see cref="DbException"/>, and a save wraps it in
        /// <see cref="PersistenceException"/>; either way it is the same advice: retry.
        /// </summary>
        private static bool IsPersistenceFailure(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is DbException or PersistenceException)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
