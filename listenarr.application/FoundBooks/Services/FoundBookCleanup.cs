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
using Listenarr.Domain.Common;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.FoundBooks.Services
{
    public sealed record FoundBookDeletion(IReadOnlyList<string> Deleted, IReadOnlyList<string> Skipped);

    /// <summary>
    /// The only code that deletes anything in a watch folder.
    ///
    /// Every deletion is fenced three ways: the path must validate as a mutation target
    /// inside the row's watch folder (no links, no escape), it must not sit inside any
    /// library root, and the file must still be the size and age the operator was shown.
    /// A file that fails any of those is skipped with the reason, never forced.
    /// Directories are removed only once empty, and never the watch folder itself.
    /// </summary>
    public sealed class FoundBookCleanup(
        IFileSystem fileSystem,
        IRootFolderRepository rootFolderRepository,
        ILogger<FoundBookCleanup> logger)
    {
        private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ".DS_Store", "Thumbs.db", "desktop.ini"
        };

        public async Task<FoundBookDeletion> DeleteFilesAsync(
            FoundBook row,
            FileSystemPathSemantics semantics,
            IEnumerable<FoundBookFileEntry> entries,
            CancellationToken cancellationToken = default)
        {
            var roots = await rootFolderRepository.GetAllAsync();
            var deleted = new List<string>();
            var skipped = new List<string>();

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!fileSystem.FileExists(entry.Path))
                {
                    continue;
                }

                if (!fileSystem.TryValidateMutationTarget(entry.Path, [row.WatchFolder], out var path, out var reason))
                {
                    skipped.Add($"{entry.Path}: {reason}");
                    continue;
                }

                if (roots.Any(root => FileSystemPathIdentity.IsSameOrInside(path, root.Path, semantics)))
                {
                    skipped.Add($"{entry.Path}: inside a library root");
                    continue;
                }

                long length;
                DateTime lastWrite;
                try
                {
                    length = fileSystem.GetFileLength(path);
                    lastWrite = fileSystem.GetLastWriteTimeUtc(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add($"{entry.Path}: {ex.Message}");
                    continue;
                }

                if (length != entry.Length || lastWrite != entry.LastWriteUtc)
                {
                    // Not the file the operator decided about. Leave it for the next
                    // scan to show them what it has become.
                    skipped.Add($"{entry.Path}: changed since it was scanned");
                    continue;
                }

                try
                {
                    fileSystem.DeleteFile(path);
                    deleted.Add(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add($"{entry.Path}: {ex.Message}");
                }
            }

            return new FoundBookDeletion(deleted, skipped);
        }

        /// <summary>
        /// Remove the directories the row's files lived in, and their ancestors, as far
        /// up as they are empty — stopping short of the watch folder. Junk files a
        /// desktop leaves behind do not count as content.
        /// </summary>
        public void RemoveEmptyDirectories(FoundBook row, FileSystemPathSemantics semantics)
        {
            var watch = FileSystemPathIdentity.Canonicalize(row.WatchFolder, semantics.Syntax);
            var starts = FoundBookFilesJson.Deserialize(row.FilesJson)
                .Select(f => Path.GetDirectoryName(f.Path))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d!)
                .Append(row.BookFolder)
                .Distinct(semantics.Comparer)
                .OrderByDescending(d => d.Length)
                .ToList();

            foreach (var start in starts)
            {
                var current = start;
                while (!string.IsNullOrWhiteSpace(current)
                    && !semantics.Comparer.Equals(FileSystemPathIdentity.Canonicalize(current, semantics.Syntax), watch)
                    && FileSystemPathIdentity.IsSameOrInside(current, watch, semantics))
                {
                    if (!TryRemoveIfEmpty(current, row.WatchFolder))
                    {
                        break;
                    }

                    current = Path.GetDirectoryName(current);
                }
            }
        }

        /// <summary>
        /// The end-of-scan sweep: junk a desktop leaves behind anywhere under the watch
        /// folder, and directories that hold nothing and have not been written for a
        /// while — long enough that a download client which just created one has had
        /// time to put something in it. The watch folder itself is never removed.
        /// </summary>
        public int Sweep(string watchFolder, FileSystemPathSemantics semantics, DateTime idleSince)
        {
            var removed = 0;
            List<string> directories;
            try
            {
                directories = fileSystem.EnumerateFiles(watchFolder, "*", SearchOption.AllDirectories)
                    .Where(f => JunkNames.Contains(Path.GetFileName(f)))
                    .Select(f => Path.GetDirectoryName(f) ?? watchFolder)
                    .Concat(EnumerateDirectoriesDeep(watchFolder))
                    .Distinct(semantics.Comparer)
                    .OrderByDescending(d => d.Length)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Sweep of {Folder} skipped", watchFolder);
                return 0;
            }

            var watch = FileSystemPathIdentity.Canonicalize(watchFolder, semantics.Syntax);
            foreach (var directory in directories)
            {
                var isWatchRoot = semantics.Comparer.Equals(FileSystemPathIdentity.Canonicalize(directory, semantics.Syntax), watch);
                if (!fileSystem.TryValidateMutationTarget(directory, [watchFolder], out var path, out _))
                {
                    continue;
                }

                List<string> entries;
                try
                {
                    entries = fileSystem.EnumerateFileSystemEntries(path).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var junk in entries.Where(e => JunkNames.Contains(Path.GetFileName(e))).ToList())
                {
                    try
                    {
                        fileSystem.DeleteFile(junk);
                        entries.Remove(junk);
                        removed++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogDebug(ex, "Could not delete {Junk}", junk);
                    }
                }

                if (isWatchRoot || entries.Count > 0)
                {
                    continue;
                }

                try
                {
                    if (fileSystem.GetLastWriteTimeUtc(path) > idleSince)
                    {
                        continue;
                    }

                    fileSystem.DeleteEmptyDirectories(path);
                    if (!fileSystem.DirectoryExists(path))
                    {
                        removed++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Could not remove {Directory}", path);
                }
            }

            return removed;
        }

        private IEnumerable<string> EnumerateDirectoriesDeep(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                IEnumerable<string> children;
                try
                {
                    children = fileSystem.EnumerateDirectories(current).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var child in children)
                {
                    yield return child;
                    stack.Push(child);
                }
            }
        }

        private bool TryRemoveIfEmpty(string directory, string watchFolder)
        {
            if (!fileSystem.DirectoryExists(directory))
            {
                return true;
            }

            if (!fileSystem.TryValidateMutationTarget(directory, [watchFolder], out var path, out var reason))
            {
                logger.LogDebug("Not removing {Directory}: {Reason}", directory, reason);
                return false;
            }

            List<string> entries;
            try
            {
                entries = fileSystem.EnumerateFileSystemEntries(path).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Not removing {Directory}", directory);
                return false;
            }

            foreach (var junk in entries.Where(e => JunkNames.Contains(Path.GetFileName(e))).ToList())
            {
                try
                {
                    fileSystem.DeleteFile(junk);
                    entries.Remove(junk);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Could not delete {Junk}", junk);
                }
            }

            if (entries.Count > 0)
            {
                return false;
            }

            // Deletes the directory itself once it is empty, through the pinned,
            // link-checked routine the rest of the app uses.
            fileSystem.DeleteEmptyDirectories(path);
            return !fileSystem.DirectoryExists(path);
        }
    }
}
