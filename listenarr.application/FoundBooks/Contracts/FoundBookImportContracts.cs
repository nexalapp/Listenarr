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
using Listenarr.Domain.FoundBooks;

namespace Listenarr.Application.FoundBooks.Contracts
{
    /// <summary>How one found book is to be brought into the library.</summary>
    /// <param name="RootPath">The library root the files are placed under.</param>
    /// <param name="Monitored">Whether the new record is monitored.</param>
    /// <param name="AllowDuplicateEdition">Add a distinct record even when the library already holds this ASIN.</param>
    /// <param name="IncludeCompanions">Move the non-audio files beside the audio too.</param>
    /// <param name="AutoAdded">Whether the scanner, not a person, chose the match.</param>
    public sealed record FoundBookImportOptions(
        string RootPath,
        bool Monitored,
        bool AllowDuplicateEdition,
        bool IncludeCompanions,
        bool AutoAdded);

    public enum FoundBookImportFailure
    {
        None,
        NotFound,
        WrongState,
        NoMatch,
        Unavailable,
        AddRefused,
        ImportFailed,
        FinishFailed,
        /// <summary>The database refused a step; nothing is known to have moved. Retry once it is back.</summary>
        Persistence
    }

    public sealed record FoundBookImportResult(
        FoundBookImportFailure Failure,
        string? Error,
        int? AudiobookId,
        FoundBook? Book,
        IReadOnlyList<string> Skipped)
    {
        public bool Success => Failure == FoundBookImportFailure.None;

        public static FoundBookImportResult Ok(int audiobookId, FoundBook book, IReadOnlyList<string>? skipped = null) =>
            new(FoundBookImportFailure.None, null, audiobookId, book, skipped ?? []);

        /// <summary>Accepted and queued: no audiobook yet, the row now importing.</summary>
        public static FoundBookImportResult Queued(FoundBook book) =>
            new(FoundBookImportFailure.None, null, null, book, []);

        public static FoundBookImportResult Fail(FoundBookImportFailure failure, string error, FoundBook? book = null) =>
            new(failure, error, null, book, []);
    }

    /// <summary>
    /// The folder a new record will live in under a root, from the naming pattern —
    /// what the Add modal shows as the destination. Recording the bare root instead
    /// leaves every book at the root until its import moves it, and a second add in
    /// that window is refused because the root is "already assigned". A host without
    /// a naming service answers with the root itself.
    /// </summary>
    public interface ILibraryDestinationPlanner
    {
        Task<LibraryDestinationPlan> PlanBookFolderAsync(AudibleBookMetadata metadata, string rootPath, CancellationToken cancellationToken = default);
    }

    /// <param name="FullPath">The folder, under the root.</param>
    /// <param name="RelativePath">The part the naming pattern produced; empty when the plan fell back to the root.</param>
    public sealed record LibraryDestinationPlan(string FullPath, string RelativePath);

    /// <summary>
    /// The one sequence that takes a found book into the library: mark the row,
    /// add or reuse the record, move the files, finish the row — and put the row
    /// back whenever any of that fails, in the same process, so no failure can
    /// depend on a browser making a follow-up call. Shared by the automatic add
    /// and a person's Add.
    /// </summary>
    public interface IFoundBookImportRunner
    {
        Task<FoundBookImportResult> RunAsync(
            FoundBook row,
            AudibleBookMetadata metadata,
            FoundBookImportOptions options,
            CancellationToken cancellationToken = default);
    }

    /// <summary>A person's Add from the Found tab.</summary>
    /// <param name="Asin">The catalogue record to add; the row's remembered match when null.</param>
    /// <param name="RootPath">The library root; the default root when null.</param>
    /// <param name="Monitored">Whether the new record is monitored.</param>
    /// <param name="SeparateBook">Add as its own record even if the library already holds the ASIN.</param>
    public sealed record FoundBookManualImportRequest(string? Asin, string? RootPath, bool Monitored, bool SeparateBook);

    /// <summary>What one pass of the import worker did, and when it next has something to do.</summary>
    public sealed record FoundBookImportDrain(int Ran, DateTime? NextDueAt);

    /// <summary>
    /// A person's Add, as a queue the row itself holds. Queueing marks the row and
    /// keeps the request on it; the worker runs whatever is due, retries a transient
    /// failure a few times with a wait between, and leaves a final failure on the
    /// row for the person to read. Nothing depends on the request that queued it
    /// staying open, and a restart resumes what was queued.
    /// </summary>
    public interface IFoundBookImportService
    {
        /// <summary>
        /// Queue the import. Success means accepted, with the row now importing; a
        /// refusal leaves the row untouched.
        /// </summary>
        Task<FoundBookImportResult> EnqueueAsync(int id, FoundBookManualImportRequest request, CancellationToken cancellationToken = default);

        /// <summary>Run every queued request whose time has come, oldest first.</summary>
        Task<FoundBookImportDrain> RunDueAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>How the queue tells its worker there is something to do.</summary>
    public interface IFoundBookImportSignal
    {
        void Wake();

        /// <summary>Waits for a wake or the timeout; true when woken.</summary>
        Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    }
}
