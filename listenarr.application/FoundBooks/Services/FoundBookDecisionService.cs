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
using Listenarr.Domain.Common;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.FoundBooks.Services
{
    public enum FoundBookDecisionFailure
    {
        None,
        NotFound,
        WrongState,
        FolderUnavailable,
        FilesRemain
    }

    public sealed record FoundBookDecisionResult(
        FoundBookDecisionFailure Failure,
        string? Error,
        FoundBook? Book,
        IReadOnlyList<string> Skipped)
    {
        public bool Success => Failure == FoundBookDecisionFailure.None;

        public static FoundBookDecisionResult Ok(FoundBook book, IReadOnlyList<string>? skipped = null) =>
            new(FoundBookDecisionFailure.None, null, book, skipped ?? []);

        public static FoundBookDecisionResult Fail(FoundBookDecisionFailure failure, string error) =>
            new(failure, error, null, []);
    }

    public interface IFoundBookDecisionService
    {
        Task<FoundBookDecisionResult> IgnoreAsync(int id, CancellationToken cancellationToken = default);
        Task<FoundBookDecisionResult> RestoreAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>Mark a row as being imported so a scan in the meantime leaves it alone.</summary>
        Task<FoundBookDecisionResult> BeginImportAsync(int id, CancellationToken cancellationToken = default);

        Task<FoundBookDecisionResult> AbortImportAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// After the manual import moved the audio: confirm it is gone, clear what it
        /// left behind, and record the row as imported into <paramref name="audiobookId"/>.
        /// </summary>
        Task<FoundBookDecisionResult> FinishImportAsync(int id, int audiobookId, CancellationToken cancellationToken = default);

        /// <summary>Delete the row's files and whatever empty directories that leaves.</summary>
        Task<FoundBookDecisionResult> DiscardAsync(int id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The operator's decisions about a found book. Every state change lands here, and
    /// the two that touch disk — finishing an import and discarding — go through
    /// <see cref="FoundBookCleanup"/> and leave a history event.
    /// </summary>
    public sealed class FoundBookDecisionService(
        IFoundBookRepository repository,
        FoundBookCleanup cleanup,
        IFileSystem fileSystem,
        IFileSystemSemanticsResolver semanticsResolver,
        IHistoryRepository historyRepository,
        IFoundBookScanProcessor scanProcessor,
        TimeProvider timeProvider,
        ILogger<FoundBookDecisionService> logger) : IFoundBookDecisionService
    {
        public const string HistorySource = "FoundBooks";

        public Task<FoundBookDecisionResult> IgnoreAsync(int id, CancellationToken cancellationToken = default) =>
            TransitionAsync(id, [FoundBookState.Pending, FoundBookState.Blocked], FoundBookState.Ignored, cancellationToken);

        public Task<FoundBookDecisionResult> RestoreAsync(int id, CancellationToken cancellationToken = default) =>
            // Back to Blocked/Settling rather than straight to Pending: the next scan
            // re-judges it against the current files and the current library.
            TransitionAsync(id, [FoundBookState.Ignored], FoundBookState.Blocked, cancellationToken, row =>
            {
                row.BlockedKind = FoundBookBlockedKind.Settling;
                row.BlockedReason = "Restored; offered again after the next scan.";
            });

        public Task<FoundBookDecisionResult> BeginImportAsync(int id, CancellationToken cancellationToken = default) =>
            TransitionAsync(id, [FoundBookState.Pending], FoundBookState.Importing, cancellationToken);

        public Task<FoundBookDecisionResult> AbortImportAsync(int id, CancellationToken cancellationToken = default) =>
            TransitionAsync(id, [FoundBookState.Importing], FoundBookState.Pending, cancellationToken);

        public async Task<FoundBookDecisionResult> FinishImportAsync(int id, int audiobookId, CancellationToken cancellationToken = default)
        {
            var row = await repository.GetAsync(id, cancellationToken);
            if (row == null)
            {
                return FoundBookDecisionResult.Fail(FoundBookDecisionFailure.NotFound, "No such found book.");
            }

            if (row.State != FoundBookState.Importing)
            {
                return FoundBookDecisionResult.Fail(FoundBookDecisionFailure.WrongState, $"The book is {row.State}, not being imported.");
            }

            var files = FoundBookFilesJson.Deserialize(row.FilesJson);
            var remaining = files.Where(f => f.IsAudio && fileSystem.FileExists(f.Path)).Select(f => f.Path).ToList();
            if (remaining.Count > 0)
            {
                // The import did not take everything. The row goes back to Pending so
                // the operator sees exactly what is still here, rather than cleanup
                // deleting companions from under a book that is still on disk.
                await repository.UpdateAsync(id, r => r.State = FoundBookState.Pending, cancellationToken);
                return FoundBookDecisionResult.Fail(
                    FoundBookDecisionFailure.FilesRemain,
                    $"{remaining.Count} audio file(s) are still in the watch folder.");
            }

            var semantics = await ResolveAsync(row.WatchFolder, cancellationToken);
            if (semantics == null)
            {
                return FoundBookDecisionResult.Fail(FoundBookDecisionFailure.FolderUnavailable, "The watch folder is unavailable.");
            }

            var deletion = await cleanup.DeleteFilesAsync(row, semantics.Value, files.Where(f => !f.IsAudio), cancellationToken);
            cleanup.RemoveEmptyDirectories(row, semantics.Value);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            await repository.UpdateAsync(id, r =>
            {
                r.State = FoundBookState.Imported;
                r.MatchedAudiobookId = audiobookId;
                r.LibraryStatus = FoundBookLibraryStatus.InLibrary;
                r.DecidedAt = now;
            }, cancellationToken);

            await RecordAsync(new History
            {
                AudiobookId = audiobookId,
                AudiobookTitle = row.DetectedTitle,
                EventType = HistoryEvents.Imported,
                Source = HistorySource,
                Message = $"Found book imported from {row.BookFolder}: {row.AudioFileCount} audio file(s)"
                    + (deletion.Deleted.Count > 0 ? $", {deletion.Deleted.Count} leftover file(s) removed" : string.Empty),
                Timestamp = now
            });

            scanProcessor.TriggerScan();
            return FoundBookDecisionResult.Ok((await repository.GetAsync(id, cancellationToken))!, deletion.Skipped);
        }

        public async Task<FoundBookDecisionResult> DiscardAsync(int id, CancellationToken cancellationToken = default)
        {
            var row = await repository.GetAsync(id, cancellationToken);
            if (row == null)
            {
                return FoundBookDecisionResult.Fail(FoundBookDecisionFailure.NotFound, "No such found book.");
            }

            var allowed = row.State is FoundBookState.Pending or FoundBookState.Ignored
                || (row.State == FoundBookState.Blocked && row.BlockedKind == FoundBookBlockedKind.Settling);
            if (!allowed)
            {
                return FoundBookDecisionResult.Fail(
                    FoundBookDecisionFailure.WrongState,
                    row.BlockedKind == FoundBookBlockedKind.OwnedByDownload
                        ? "A download record still owns these files; remove the download first."
                        : $"The book is {row.State} and cannot be discarded.");
            }

            var semantics = await ResolveAsync(row.WatchFolder, cancellationToken);
            if (semantics == null)
            {
                return FoundBookDecisionResult.Fail(FoundBookDecisionFailure.FolderUnavailable, "The watch folder is unavailable.");
            }

            var files = FoundBookFilesJson.Deserialize(row.FilesJson);
            var deletion = await cleanup.DeleteFilesAsync(row, semantics.Value, files, cancellationToken);
            cleanup.RemoveEmptyDirectories(row, semantics.Value);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            await repository.UpdateAsync(id, r =>
            {
                r.State = FoundBookState.Discarded;
                r.DecidedAt = now;
            }, cancellationToken);

            await RecordAsync(new History
            {
                AudiobookTitle = row.DetectedTitle,
                EventType = HistoryEvents.FileDeleted,
                Source = HistorySource,
                Message = $"Found book discarded from {row.BookFolder}: {deletion.Deleted.Count} file(s) deleted"
                    + (deletion.Skipped.Count > 0 ? $", {deletion.Skipped.Count} skipped" : string.Empty),
                Data = string.Join("\n", deletion.Deleted.Concat(deletion.Skipped.Select(s => "skipped: " + s))),
                Timestamp = now
            });

            scanProcessor.TriggerScan();
            return FoundBookDecisionResult.Ok((await repository.GetAsync(id, cancellationToken))!, deletion.Skipped);
        }

        private async Task<FoundBookDecisionResult> TransitionAsync(
            int id,
            FoundBookState[] from,
            FoundBookState to,
            CancellationToken cancellationToken,
            Action<FoundBook>? also = null)
        {
            var row = await repository.GetAsync(id, cancellationToken);
            if (row == null)
            {
                return FoundBookDecisionResult.Fail(FoundBookDecisionFailure.NotFound, "No such found book.");
            }

            if (!from.Contains(row.State))
            {
                return FoundBookDecisionResult.Fail(FoundBookDecisionFailure.WrongState, $"The book is {row.State}.");
            }

            if (row.State == FoundBookState.Blocked && row.BlockedKind == FoundBookBlockedKind.OwnedByDownload && to != FoundBookState.Ignored)
            {
                return FoundBookDecisionResult.Fail(FoundBookDecisionFailure.WrongState, "A download record still owns these files.");
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            await repository.UpdateAsync(id, r =>
            {
                r.State = to;
                r.DecidedAt = to is FoundBookState.Ignored or FoundBookState.Blocked ? now : r.DecidedAt;
                if (to == FoundBookState.Pending)
                {
                    r.BlockedKind = FoundBookBlockedKind.None;
                    r.BlockedReason = null;
                }

                also?.Invoke(r);
            }, cancellationToken);

            return FoundBookDecisionResult.Ok((await repository.GetAsync(id, cancellationToken))!);
        }

        private async Task<FileSystemPathSemantics?> ResolveAsync(string watchFolder, CancellationToken cancellationToken)
        {
            if (!fileSystem.DirectoryExists(watchFolder))
            {
                return null;
            }

            var resolution = await semanticsResolver.ResolveAsync(watchFolder, FileSystemCaseSensitivityMode.Auto, cancellationToken);
            return resolution.State == PathIdentityState.Valid ? resolution.Semantics : null;
        }

        private async Task RecordAsync(History entry)
        {
            try
            {
                await historyRepository.AddAsync(entry, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && WorkerExceptionClassifier.IsNonFatal(ex))
            {
                logger.LogWarning(ex, "Could not write found-book history");
            }
        }
    }
}
