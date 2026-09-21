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
using Listenarr.Application.Common;
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Application.Search.Strategies;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.FoundBooks.Services
{
    public sealed class FoundBookImportService(
        IFoundBookRepository repository,
        IRootFolderRepository rootFolderRepository,
        ISearchService searchService,
        MetadataStrategyCoordinator metadataCoordinator,
        IConfigurationService configurationService,
        IFoundBookDecisionService decisions,
        IFoundBookImportRunner runner,
        IFoundBookImportSignal signal,
        IHubBroadcaster hubBroadcaster,
        TimeProvider timeProvider,
        ILogger<FoundBookImportService> logger) : IFoundBookImportService
    {
        /// <summary>
        /// A database that refuses for a moment gets a second and a third go, spaced
        /// out; anything the third also cannot do is left for the person to read.
        /// </summary>
        public static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task<FoundBookImportResult> EnqueueAsync(int id, FoundBookManualImportRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var row = await repository.GetAsync(id, cancellationToken);
            if (row == null)
            {
                return FoundBookImportResult.Fail(FoundBookImportFailure.NotFound, "No such found book.");
            }

            // The cheap refusals come before the row is marked: no match means nothing
            // to add, and there is no point queueing what cannot start.
            var asin = FirstNonBlank(request.Asin, row.MatchAsin);
            if (asin == null)
            {
                return FoundBookImportResult.Fail(FoundBookImportFailure.NoMatch, "Choose a catalogue match for this book first.", row);
            }

            var rootPath = await ResolveRootAsync(request.RootPath);
            if (rootPath == null)
            {
                return FoundBookImportResult.Fail(FoundBookImportFailure.AddRefused, "No library root folder is configured.", row);
            }

            var queued = new FoundBookQueuedImport(
                JsonSerializer.Serialize(request with { Asin = asin, RootPath = rootPath }, JsonOptions),
                Attempt: 0,
                NotBefore: null);
            var begun = await decisions.BeginImportAsync(id, queued, cancellationToken);
            if (!begun.Success)
            {
                return FoundBookImportResult.Fail(
                    begun.Failure == FoundBookDecisionFailure.NotFound ? FoundBookImportFailure.NotFound : FoundBookImportFailure.WrongState,
                    begun.Error ?? "The book cannot be imported in its current state.",
                    row);
            }

            signal.Wake();
            // Another open Found tab learns the row is taken the same way it learns
            // the outcome.
            await BroadcastAsync(cancellationToken);
            return FoundBookImportResult.Queued(begun.Book!);
        }

        public async Task<FoundBookImportDrain> RunDueAsync(CancellationToken cancellationToken = default)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var queued = (await repository.GetAllAsync(cancellationToken))
                .Where(row => row.State == FoundBookState.Importing && row.ImportRequestJson != null)
                .OrderBy(row => row.ImportStartedAt)
                .ToList();

            var ran = 0;
            foreach (var row in queued.Where(row => row.ImportNotBefore == null || row.ImportNotBefore <= now))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RunQueuedAsync(row, cancellationToken);
                ran++;
            }

            // Re-read: a run that failed transiently has just queued its own retry.
            var nextDue = (await repository.GetAllAsync(cancellationToken))
                .Where(row => row.State == FoundBookState.Importing && row.ImportRequestJson != null && row.ImportNotBefore > now)
                .Min(row => row.ImportNotBefore);
            return new FoundBookImportDrain(ran, nextDue);
        }

        private async Task RunQueuedAsync(FoundBook row, CancellationToken cancellationToken)
        {
            var request = Deserialize(row.ImportRequestJson);
            var attempt = row.ImportAttempts + 1;
            FoundBookImportResult result;
            try
            {
                if (request == null)
                {
                    result = await FailAsync(row, FoundBookImportFailure.ImportFailed, "The queued request could not be read.", cancellationToken);
                }
                else if (attempt > RetryDelays.Length + 1)
                {
                    // Every attempt has been used and the row is still queued: the abort
                    // that should have ended it could not be written. End it here rather
                    // than run it once a minute for ever.
                    result = await FailAsync(row, FoundBookImportFailure.Persistence,
                        $"Gave up after {row.ImportAttempts} attempts; the last could not be recorded. Try again once the database is well.", cancellationToken);
                }
                else
                {
                    result = await RunAsync(row, request, attempt, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex) || ex is OperationCanceledException)
            {
                // The runner puts the row back itself; this is for what fails around it.
                // A cancellation nobody asked for is a timeout below, and a failure.
                logger.LogError(ex, "Queued import of found book {Id} failed outside the import", row.Id);
                result = await FailAsync(row, FoundBookImportFailure.ImportFailed, ex is OperationCanceledException ? "A step timed out before it finished." : ex.Message, cancellationToken);
            }

            if (result.Failure == FoundBookImportFailure.Persistence && attempt <= RetryDelays.Length && request != null)
            {
                var delay = RetryDelays[attempt - 1];
                var again = new FoundBookQueuedImport(row.ImportRequestJson!, attempt, timeProvider.GetUtcNow().UtcDateTime + delay);
                var requeued = await decisions.BeginImportAsync(row.Id, again, cancellationToken);
                logger.LogWarning(
                    "Import of found book {Id} hit a database failure on attempt {Attempt}; {Outcome}",
                    row.Id, attempt, requeued.Success ? $"retrying in {delay}" : $"not retried: {requeued.Error}");
            }
            else if (result.Success)
            {
                logger.LogInformation("Imported found book {Id} as audiobook {AudiobookId} on attempt {Attempt}", row.Id, result.AudiobookId, attempt);
            }
            else
            {
                logger.LogWarning("Import of found book {Id} failed on attempt {Attempt}: {Error}", row.Id, attempt, result.Error);
            }

            await BroadcastAsync(cancellationToken);
        }

        private async Task<FoundBookImportResult> RunAsync(FoundBook row, FoundBookManualImportRequest request, int attempt, CancellationToken cancellationToken)
        {
            await repository.UpdateAsync(row.Id, r => r.ImportAttempts = attempt, cancellationToken);

            var sources = await searchService.GetEnabledMetadataSourcesAsync();
            if (sources.Count == 0)
            {
                return await FailAsync(row, FoundBookImportFailure.NoMatch, "No metadata source is enabled.", cancellationToken);
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            var (metadata, _) = await metadataCoordinator.FetchMetadataAsync(request.Asin!, sources, null, settings.DefaultSearchRegion);
            if (metadata == null)
            {
                return await FailAsync(row, FoundBookImportFailure.NoMatch, $"The catalogue has no record for {request.Asin}.", cancellationToken);
            }

            // A pack's loose files share one directory; the companion pass would sweep
            // the neighbours' covers and notes into this book, so it stays off for those.
            var shared = FoundBookFolderSharing.SharedRows(await repository.GetAllAsync(cancellationToken));

            return await runner.RunAsync(row, metadata, new FoundBookImportOptions(
                RootPath: request.RootPath!,
                Monitored: request.Monitored,
                AllowDuplicateEdition: request.SeparateBook,
                IncludeCompanions: !shared.Contains(row.Id),
                AutoAdded: false), cancellationToken);
        }

        private async Task<FoundBookImportResult> FailAsync(FoundBook row, FoundBookImportFailure failure, string error, CancellationToken cancellationToken)
        {
            var aborted = await decisions.AbortImportAsync(row.Id, error, cancellationToken);
            return FoundBookImportResult.Fail(failure, error, aborted.Book ?? row);
        }

        private async Task BroadcastAsync(CancellationToken cancellationToken)
        {
            try
            {
                var rows = await repository.GetAllAsync(cancellationToken);
                await hubBroadcaster.BroadcastAsync(
                    RealtimeHubTarget.Settings,
                    FoundBookScanService.ChangedEvent,
                    new
                    {
                        pending = rows.Count(r => r.State == FoundBookState.Pending),
                        blocked = rows.Count(r => r.State == FoundBookState.Blocked)
                    },
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && WorkerExceptionClassifier.IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not broadcast the found-book change");
            }
        }

        private async Task<string?> ResolveRootAsync(string? requested) =>
            FirstNonBlank(requested, (await rootFolderRepository.GetDefaultAsync())?.Path)
            ?? (await rootFolderRepository.GetAllAsync()).FirstOrDefault()?.Path;

        private static FoundBookManualImportRequest? Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var request = JsonSerializer.Deserialize<FoundBookManualImportRequest>(json, JsonOptions);
                return request is { Asin.Length: > 0, RootPath.Length: > 0 } ? request : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
    }
}
