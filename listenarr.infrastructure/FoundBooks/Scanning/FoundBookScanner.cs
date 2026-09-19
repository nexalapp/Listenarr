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
using Listenarr.Application.FoundBooks.Models;
using Listenarr.Domain.Common;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.FoundBooks.Scanning
{
    /// <summary>
    /// Walks a watch folder and clusters what it finds into books.
    ///
    /// Grouping is the Library Import grouping: within one directory, files that share
    /// an album and artist are one book, and files with no usable tags fall back to a
    /// shared filename stem. On top of that, clusters in neighbouring directories that
    /// name the same book are merged, because a pack often leaves part of a book loose
    /// beside a folder holding the rest.
    /// </summary>
    public sealed partial class FoundBookScanner(
        IFileSystem fileSystem,
        IFoundBookProbe probe,
        IConfigurationService configurationService,
        ILogger<FoundBookScanner> logger) : IFoundBookScanner
    {
        private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ".DS_Store", "Thumbs.db", "desktop.ini"
        };

        /// <summary>Extensions a download client gives a file it is still writing.</summary>
        private static readonly HashSet<string> InProgressExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".part", ".partial", ".!qb", ".crdownload", ".aria2", ".tmp", ".lock", ".nzb.queued"
        };

        private sealed record ScannedFile(string Path, string Directory, long Length, DateTime LastWriteUtc, bool IsAudio, bool IsInProgress);

        public async Task<FoundBookScanReport> ScanAsync(
            FoundBookWatchFolder watchFolder,
            IReadOnlyDictionary<string, FoundBookKnownFile> knownFiles,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(watchFolder);
            ArgumentNullException.ThrowIfNull(knownFiles);

            var root = watchFolder.Path;
            var semantics = watchFolder.Semantics;
            var settings = await configurationService.GetApplicationSettingsAsync();
            var concurrency = Math.Clamp(settings?.UnmatchedScanConcurrency ?? 2, 1, 8);
            var warnings = new List<string>();

            var files = Walk(root, warnings);
            if (files == null)
            {
                return new FoundBookScanReport(root, [], warnings, Succeeded: false);
            }
            var byDirectory = files
                .GroupBy(f => f.Directory, semantics.Comparer)
                .ToList();

            var clusters = new List<Cluster>();
            foreach (var directory in byDirectory)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var audio = directory.Where(f => f.IsAudio && !f.IsInProgress).ToList();
                if (audio.Count == 0)
                {
                    continue;
                }

                var probes = await ProbeAsync(audio, knownFiles, semantics, concurrency, cancellationToken);
                var tagsByFile = probes.ToDictionary(
                    pair => pair.Key,
                    pair => new PathParsedMetadata
                    {
                        Title = pair.Value.Album,
                        Author = pair.Value.AlbumArtist ?? pair.Value.Artist
                    },
                    semantics.Comparer);

                var groups = UnmatchedScanProcessor.BuildGroupedFilesForFolder(
                    audio.Select(f => f.Path),
                    directory.Key,
                    semantics,
                    tagsByFile);

                foreach (var group in groups)
                {
                    var members = group
                        .Select(path => audio.First(f => semantics.Comparer.Equals(f.Path, path)))
                        .ToList();
                    clusters.Add(new Cluster(members, probes));
                }
            }

            MergeUntagged(clusters, semantics);
            Merge(clusters, semantics);
            AttachCompanions(clusters, files, semantics);

            var inProgressDirectories = files
                .Where(f => f.IsInProgress)
                .Select(f => f.Directory)
                .ToHashSet(semantics.Comparer);
            var candidates = clusters
                .Select(cluster => Build(cluster, root, semantics, inProgressDirectories))
                .OrderBy(c => c.Author, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Series, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            logger.LogInformation(
                "Found-book scan of {Folder}: {Files} files, {Clusters} clusters",
                root,
                files.Count,
                candidates.Count);

            return new FoundBookScanReport(root, candidates, warnings);
        }

        /// <returns>Every file under the root, or null when the root could not be listed.</returns>
        private List<ScannedFile>? Walk(string root, List<string> warnings)
        {
            var result = new List<ScannedFile>();
            IEnumerable<string> paths;
            try
            {
                paths = fileSystem.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not list {root}: {ex.Message}");
                return null;
            }

            foreach (var path in paths)
            {
                var name = Path.GetFileName(path);
                if (JunkNames.Contains(name) || name.StartsWith("._", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    result.Add(new ScannedFile(
                        path,
                        Path.GetDirectoryName(path) ?? root,
                        fileSystem.GetFileLength(path),
                        fileSystem.GetLastWriteTimeUtc(path),
                        FileUtils.IsAudioFile(path),
                        IsInProgress(name)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file that vanished between the listing and the stat is a
                    // download client tidying up; the next scan sees the new shape.
                    logger.LogDebug(ex, "Skipping {Path} during found-book scan", path);
                }
            }

            return result;
        }

        private static bool IsInProgress(string name)
        {
            var ext = Path.GetExtension(name);
            return InProgressExtensions.Contains(ext)
                || name.EndsWith(".!qB", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<string, FoundBookProbeSnapshot>> ProbeAsync(
            IReadOnlyList<ScannedFile> audio,
            IReadOnlyDictionary<string, FoundBookKnownFile> knownFiles,
            FileSystemPathSemantics semantics,
            int concurrency,
            CancellationToken cancellationToken)
        {
            var results = new System.Collections.Concurrent.ConcurrentDictionary<string, FoundBookProbeSnapshot>(semantics.Comparer);
            await Parallel.ForEachAsync(
                audio,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken },
                async (file, token) =>
                {
                    if (knownFiles.TryGetValue(file.Path, out var known)
                        && known.Length == file.Length
                        && known.LastWriteUtc == file.LastWriteUtc)
                    {
                        results[file.Path] = known.Probe;
                        return;
                    }

                    try
                    {
                        results[file.Path] = await probe.ProbeAsync(file.Path, token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && WorkerExceptionClassifier.IsNonFatal(ex))
                    {
                        logger.LogWarning(ex, "Probe failed for {Path}", file.Path);
                        results[file.Path] = FfprobeFoundBookProbe.Failed(ex.Message);
                    }
                });

            return new Dictionary<string, FoundBookProbeSnapshot>(results, semantics.Comparer);
        }
    }
}
