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
using Listenarr.Application.FoundBooks.Models;
using Listenarr.Domain.Common;
using Listenarr.Domain.FoundBooks;

namespace Listenarr.Application.FoundBooks.Contracts
{
    /// <summary>Reads one audio file's tags, length and chapter count. Implemented over ffprobe.</summary>
    public interface IFoundBookProbe
    {
        Task<FoundBookProbeSnapshot> ProbeAsync(string path, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Walks one watch folder and clusters its files into books. Read-only: it opens
    /// files to probe them and never writes.
    /// </summary>
    public interface IFoundBookScanner
    {
        /// <param name="watchFolder">The folder to walk, with its resolved semantics.</param>
        /// <param name="knownFiles">
        /// Probe results from the last scan keyed by path. A file whose length and mtime
        /// still match is not probed again.
        /// </param>
        /// <param name="cancellationToken">Stops the walk and any probe in flight.</param>
        Task<FoundBookScanReport> ScanAsync(
            FoundBookWatchFolder watchFolder,
            IReadOnlyDictionary<string, FoundBookKnownFile> knownFiles,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The folders to watch: the configured list, or every enabled download client's
    /// completed path translated through its remote path mappings when the list is empty.
    /// </summary>
    public interface IFoundBookWatchFolderResolver
    {
        Task<FoundBookWatchFolders> ResolveAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A watch folder with its filesystem semantics resolved. The semantics come from the
    /// same resolver the library uses, so path comparison inside the folder is never a
    /// host-default guess.
    /// </summary>
    public sealed record FoundBookWatchFolder(string Path, FileSystemPathSemantics Semantics);

    /// <summary>
    /// The folders a scan will walk, plus the configured ones that could not be used
    /// this time — missing, or on a filesystem that could not be characterised. Rows
    /// for an unavailable folder are kept: a mount that is away for an hour should not
    /// cost the operator every Ignore decision in it.
    /// </summary>
    public sealed record FoundBookWatchFolders(
        IReadOnlyList<FoundBookWatchFolder> Folders,
        IReadOnlyList<string> Unavailable,
        IReadOnlyList<string> Warnings,
        bool FromSettings);

    public interface IFoundBookRepository
    {
        Task<IReadOnlyList<FoundBook>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<FoundBook>> GetByWatchFolderAsync(string watchFolder, CancellationToken cancellationToken = default);
        Task<FoundBook?> GetAsync(int id, CancellationToken cancellationToken = default);
        Task<FoundBook> AddAsync(FoundBook book, CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(int id, Action<FoundBook> mutate, CancellationToken cancellationToken = default);
        Task<int> DeleteAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Runs a scan across every watch folder and reconciles the rows. One at a time:
    /// a scan requested from the UI while the periodic one is running joins it rather
    /// than starting a second.
    /// </summary>
    public interface IFoundBookScanProcessor
    {
        Task RunCycleAsync(CancellationToken cancellationToken);

        /// <summary>Start a scan now unless one is already running. Returns whether one was started.</summary>
        bool TriggerScan();

        bool IsScanning { get; }
        DateTime? LastScanCompletedAt { get; }
    }
}
