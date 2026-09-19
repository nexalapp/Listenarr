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
using Listenarr.Application.FoundBooks.Models;
using Listenarr.Domain.Common;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.FoundBooks.Services
{
    public sealed record FoundBookScanSummary(
        IReadOnlyList<FoundBookWatchFolder> WatchFolders,
        int Pending,
        int Blocked,
        IReadOnlyList<string> Warnings);

    public interface IFoundBookScanService
    {
        Task<FoundBookScanSummary> ScanAllAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One scan cycle: resolve the watch folders, scan each, and reconcile what was
    /// found against the rows from last time and against the library.
    /// </summary>
    public sealed class FoundBookScanService(
        IFoundBookWatchFolderResolver watchFolderResolver,
        IFoundBookScanner scanner,
        IFoundBookRepository repository,
        IAudiobookRepository audiobookRepository,
        IDownloadRepository downloadRepository,
        FoundBookCleanup cleanup,
        IHubBroadcaster hubBroadcaster,
        TimeProvider timeProvider,
        ILogger<FoundBookScanService> logger) : IFoundBookScanService
    {
        /// <summary>
        /// How long a cluster's newest file must have been untouched before a first
        /// sighting is offered. A second sighting with the same signature is offered
        /// regardless of age.
        /// </summary>
        public static readonly TimeSpan SettleWindow = TimeSpan.FromMinutes(10);

        public const string ChangedEvent = "FoundBooksChanged";

        public async Task<FoundBookScanSummary> ScanAllAsync(CancellationToken cancellationToken = default)
        {
            var resolved = await watchFolderResolver.ResolveAsync(cancellationToken);
            var warnings = new List<string>(resolved.Warnings);
            var library = await audiobookRepository.GetAllAsync();
            var ownedPaths = await LoadDownloadOwnedPathsAsync();

            var existing = await repository.GetAllAsync(cancellationToken);
            var stale = existing
                .Where(row => !resolved.Folders.Any(f => f.Semantics.Comparer.Equals(f.Path, row.WatchFolder)))
                .Where(row => !resolved.Unavailable.Any(u => PathsLookAlike(u, row.WatchFolder)))
                .Select(row => row.Id)
                .ToList();
            if (stale.Count > 0)
            {
                await repository.DeleteAsync(stale, cancellationToken);
            }

            var pending = 0;
            var blocked = 0;
            foreach (var folder in resolved.Folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = existing.Where(row => folder.Semantics.Comparer.Equals(row.WatchFolder, folder.Path)).ToList();
                try
                {
                    var (p, b) = await ScanFolderAsync(folder, rows, library, ownedPaths, warnings, cancellationToken);
                    pending += p;
                    blocked += b;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && WorkerExceptionClassifier.IsNonFatal(ex))
                {
                    logger.LogError(ex, "Found-book scan of {Folder} failed", folder.Path);
                    warnings.Add($"{folder.Path}: {ex.Message}");
                }
            }

            await hubBroadcaster.BroadcastAsync(
                RealtimeHubTarget.Settings,
                ChangedEvent,
                new { pending, blocked, watchFolders = resolved.Folders.Count },
                cancellationToken);

            return new FoundBookScanSummary(resolved.Folders, pending, blocked, warnings);
        }

        /// <summary>
        /// Paths a Listenarr download record still owns, with why. The import pipeline
        /// is working on Queued through ImportPending; Moved with the files still present
        /// is a copy kept for seeding. Failed and ImportBlocked are exactly the leftovers
        /// this feature exists to offer, so they are not owners.
        /// </summary>
        private async Task<IReadOnlyList<(string Path, string Reason)>> LoadDownloadOwnedPathsAsync()
        {
            var downloads = await downloadRepository.GetAllAsync();
            var owned = new List<(string, string)>();
            foreach (var download in downloads)
            {
                var reason = download.Status switch
                {
                    DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Paused
                        => "still downloading",
                    DownloadStatus.Completed or DownloadStatus.Processing or DownloadStatus.Ready or DownloadStatus.ImportPending
                        => "being imported by the download pipeline",
                    DownloadStatus.Moved => "kept for seeding by the download client",
                    _ => null
                };
                if (reason == null)
                {
                    continue;
                }

                // FinalPath is the local spelling once the pipeline has resolved it; the
                // client's own content path is kept in metadata and may be the remote
                // spelling, which then simply fails to overlap anything here.
                foreach (var path in new[] { download.FinalPath, download.GetMetadataString("ClientContentPath") }
                    .Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    owned.Add((path!, $"{reason} ({download.Title})"));
                }
            }

            return owned;
        }

        private static string? DownloadOwner(
            FoundBookCandidate candidate,
            FoundBookWatchFolder folder,
            IReadOnlyList<(string Path, string Reason)> ownedPaths)
        {
            foreach (var (path, reason) in ownedPaths)
            {
                string canonical;
                try
                {
                    canonical = FileSystemPathIdentity.Canonicalize(path, folder.Semantics.Syntax);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                var overlaps = FileSystemPathIdentity.IsSameOrInside(candidate.BookFolder, canonical, folder.Semantics)
                    || candidate.AudioFiles.Any(f => FileSystemPathIdentity.IsSameOrInside(f.Path, canonical, folder.Semantics))
                    || FileSystemPathIdentity.IsSameOrInside(canonical, candidate.BookFolder, folder.Semantics);
                if (overlaps)
                {
                    return reason;
                }
            }

            return null;
        }

        private async Task<(int Pending, int Blocked)> ScanFolderAsync(
            FoundBookWatchFolder folder,
            IReadOnlyList<FoundBook> rows,
            IReadOnlyList<Audiobook> library,
            IReadOnlyList<(string Path, string Reason)> ownedPaths,
            List<string> warnings,
            CancellationToken cancellationToken)
        {
            var known = new Dictionary<string, FoundBookKnownFile>(folder.Semantics.Comparer);
            foreach (var entry in rows.SelectMany(row => FoundBookFilesJson.Deserialize(row.FilesJson)))
            {
                if (entry.Probe != null)
                {
                    known[entry.Path] = new FoundBookKnownFile(entry.Length, entry.LastWriteUtc, entry.Probe);
                }
            }

            var report = await scanner.ScanAsync(folder, known, cancellationToken);
            warnings.AddRange(report.Warnings);
            if (!report.Succeeded)
            {
                // Nothing was seen, which is not the same as nothing being there.
                return (rows.Count(r => r.State == FoundBookState.Pending), rows.Count(r => r.State == FoundBookState.Blocked));
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var byKey = rows.ToDictionary(row => row.ClusterKey, StringComparer.Ordinal);
            var seen = new HashSet<int>();
            var pending = 0;
            var blocked = 0;

            foreach (var candidate in report.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = FoundBookLibraryMatcher.Match(candidate.Asin, candidate.Title, candidate.Author, library);
                var owner = DownloadOwner(candidate, folder, ownedPaths);

                if (byKey.TryGetValue(candidate.ClusterKey, out var row))
                {
                    seen.Add(row.Id);
                    var unchanged = string.Equals(row.Signature, candidate.Signature, StringComparison.Ordinal);
                    await repository.UpdateAsync(row.Id, r =>
                    {
                        Apply(r, candidate, match);
                        r.LastSeenAt = now;
                        if (!unchanged)
                        {
                            r.SignatureChangedAt = now;
                        }

                        if (r.State is FoundBookState.Pending or FoundBookState.Blocked)
                        {
                            SetOffer(r, candidate, stable: unchanged || Settled(candidate, now), owner);
                        }
                    }, cancellationToken);
                    Count(row.State, unchanged || Settled(candidate, now), candidate, owner, ref pending, ref blocked);
                }
                else
                {
                    var created = new FoundBook
                    {
                        ClusterKey = candidate.ClusterKey,
                        WatchFolder = folder.Path,
                        FirstSeenAt = now,
                        LastSeenAt = now,
                        SignatureChangedAt = now
                    };
                    Apply(created, candidate, match);
                    SetOffer(created, candidate, stable: Settled(candidate, now), owner);
                    await repository.AddAsync(created, cancellationToken);
                    if (created.State == FoundBookState.Pending)
                    {
                        pending++;
                    }
                    else
                    {
                        blocked++;
                    }
                }
            }

            var gone = rows.Where(row => !seen.Contains(row.Id)).Select(row => row.Id).ToList();
            if (gone.Count > 0)
            {
                await repository.DeleteAsync(gone, cancellationToken);
            }

            // Junk and long-empty directories are nobody's book; a scan is the moment
            // to clear them, so a folder whose last book was taken ends up empty.
            var swept = cleanup.Sweep(folder.Path, folder.Semantics, now - SettleWindow);
            if (swept > 0)
            {
                logger.LogInformation("Swept {Count} junk file(s) and empty director(ies) from {Folder}", swept, folder.Path);
            }

            return (pending, blocked);
        }

        private static void Count(
            FoundBookState previousState,
            bool stable,
            FoundBookCandidate candidate,
            string? owner,
            ref int pending,
            ref int blocked)
        {
            if (previousState is not (FoundBookState.Pending or FoundBookState.Blocked))
            {
                return;
            }

            if (stable && !candidate.DownloadInProgress && owner == null)
            {
                pending++;
            }
            else
            {
                blocked++;
            }
        }

        /// <summary>
        /// An unavailable folder has no resolved semantics to compare with, so the
        /// configured spelling is matched loosely: separators normalised, case ignored.
        /// Erring towards keeping a row is the safe direction here.
        /// </summary>
        private static bool PathsLookAlike(string configured, string stored)
        {
            static string Norm(string p) => p.Replace('\\', '/').TrimEnd('/');
            return string.Equals(Norm(configured), Norm(stored), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Settled(FoundBookCandidate candidate, DateTime now) =>
            now - candidate.NewestWriteUtc >= SettleWindow;

        private static void SetOffer(FoundBook row, FoundBookCandidate candidate, bool stable, string? owner)
        {
            if (owner != null)
            {
                row.State = FoundBookState.Blocked;
                row.BlockedKind = FoundBookBlockedKind.OwnedByDownload;
                row.BlockedReason = $"A download record still owns these files: {owner}.";
            }
            else if (candidate.DownloadInProgress)
            {
                row.State = FoundBookState.Blocked;
                row.BlockedKind = FoundBookBlockedKind.Downloading;
                row.BlockedReason = "A download is still in progress in this folder.";
            }
            else if (!stable)
            {
                row.State = FoundBookState.Blocked;
                row.BlockedKind = FoundBookBlockedKind.Settling;
                row.BlockedReason = "The files are still changing; offered once they settle.";
            }
            else
            {
                row.State = FoundBookState.Pending;
                row.BlockedKind = FoundBookBlockedKind.None;
                row.BlockedReason = null;
            }
        }

        private static void Apply(FoundBook row, FoundBookCandidate candidate, FoundBookLibraryMatch match)
        {
            row.Signature = candidate.Signature;
            row.BookFolder = candidate.BookFolder;
            row.FilesJson = FoundBookFilesJson.Serialize(candidate.Files);
            row.AudioFileCount = candidate.AudioFileCount;
            row.TotalBytes = candidate.TotalBytes;
            row.TotalDurationSeconds = candidate.TotalDurationSeconds;
            row.Format = candidate.Format;
            row.DetectedTitle = Clip(candidate.Title, 500);
            row.DetectedAuthor = Clip(candidate.Author, 500);
            row.DetectedSeries = Clip(candidate.Series, 500);
            row.DetectedSeriesPosition = Clip(candidate.SeriesPosition, 32);
            row.DetectedNarrator = Clip(candidate.Narrator, 500);
            row.DetectedYear = Clip(candidate.Year, 16);
            row.DetectedAsin = Clip(candidate.Asin, 32);
            row.Completeness = candidate.Completeness;
            row.CompletenessReason = Clip(candidate.CompletenessReason, 1000);
            row.LibraryStatus = match.Status;
            row.MatchedAudiobookId = match.AudiobookId;
        }

        private static string? Clip(string? value, int max) =>
            value == null ? null : value.Length <= max ? value : value[..max];
    }
}
