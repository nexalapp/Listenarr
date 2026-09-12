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
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// Where the tag table thinks each file belongs, and how much of that is worth
    /// reading in a table cell.
    /// </summary>
    /// <remarks>
    /// Separate from the tag reading because it answers a different question against a
    /// different service: organizing decides these, the tag writer decides the rest.
    /// </remarks>
    public sealed partial class LibraryTagIndexService
    {
        /// <summary>
        /// One expected path as the table shows it: the folder and the filename apart,
        /// each with whether organizing would change it.
        /// </summary>
        /// <remarks>
        /// The two halves are attributed by an ordinal comparison, but only once
        /// organizing has already said the path as a whole would change. That ordering
        /// matters: the whole-path answer is the one computed against the filesystem's
        /// real case sensitivity, so a case-only difference the filesystem does not
        /// consider a difference never reaches the split at all.
        /// </remarks>
        private static (string? Folder, string? FileName, bool FolderChanged, bool FileNameChanged)
            SplitExpectation(
                string currentPath,
                PathExpectation? expectation,
                IReadOnlyList<string> libraryRoots)
        {
            if (expectation == null)
            {
                return (null, null, false, false);
            }

            var expectedFolder = ToLibraryRelativePath(
                Path.GetDirectoryName(expectation.ExpectedPath),
                libraryRoots);
            var expectedFileName = Path.GetFileName(expectation.ExpectedPath);

            if (!expectation.Changed)
            {
                return (expectedFolder, expectedFileName, false, false);
            }

            var folderChanged = !string.Equals(
                Path.GetDirectoryName(currentPath),
                Path.GetDirectoryName(expectation.ExpectedPath),
                StringComparison.Ordinal);
            var fileNameChanged = !string.Equals(
                Path.GetFileName(currentPath),
                expectedFileName,
                StringComparison.Ordinal);

            return (expectedFolder, expectedFileName, folderChanged, fileNameChanged);
        }

        /// <summary>Where organizing says one file belongs, and whether that is where it is.</summary>
        private sealed record PathExpectation(string ExpectedPath, bool Changed);

        /// <summary>
        /// The expected path of every file of these books, keyed by file id.
        /// </summary>
        /// <remarks>
        /// Answered by the same service the Organize button runs, rather than by a second
        /// rendering of the naming pattern here. A table that highlighted a path the
        /// organizer would then leave alone — or left one alone that it would move — is
        /// worse than a table with no path column, because it would be believed.
        /// <para>
        /// Every failure is swallowed to an absent expectation. Organizing is a separate
        /// concern that can be unavailable for its own reasons — no rename service
        /// registered, a root whose filesystem identity cannot be established — and none
        /// of them is a reason to refuse to show the library's tags.
        /// </para>
        /// </remarks>
        private async Task<IReadOnlyDictionary<int, PathExpectation>> BuildPathExpectationsAsync(
            IReadOnlyList<int> audiobookIds,
            CancellationToken cancellationToken)
        {
            var expectations = new Dictionary<int, PathExpectation>();
            if (renameService == null || audiobookIds.Count == 0)
            {
                return expectations;
            }

            for (var offset = 0; offset < audiobookIds.Count; offset += PathExpectationBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = audiobookIds
                    .Skip(offset)
                    .Take(PathExpectationBatchSize)
                    .ToArray();

                var previews = await TryPreviewRenameAsync(batch, cancellationToken);
                if (previews == null)
                {
                    // The batch failed as a unit, so each book is asked on its own: one
                    // book with an unresolvable root should not blank the path column for
                    // the forty-nine beside it.
                    foreach (var audiobookId in batch)
                    {
                        var single = await TryPreviewRenameAsync([audiobookId], cancellationToken);
                        if (single != null)
                        {
                            Record(single);
                        }
                    }

                    continue;
                }

                Record(previews);
            }

            return expectations;

            void Record(IEnumerable<RenamePreview> previews)
            {
                foreach (var rename in previews.SelectMany(preview => preview.FileRenames))
                {
                    if (!string.IsNullOrWhiteSpace(rename.NewPath))
                    {
                        expectations[rename.FileId] = new PathExpectation(
                            rename.NewPath,
                            rename.Changed);
                    }
                }
            }
        }

        private async Task<List<RenamePreview>?> TryPreviewRenameAsync(
            int[] audiobookIds,
            CancellationToken cancellationToken)
        {
            try
            {
                return await renameService!.PreviewRenameAsync(audiobookIds, cancellationToken);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                logger.LogDebug(
                    ex,
                    "Could not work out the expected paths of {Count} audiobook(s) for the library tag table",
                    audiobookIds.Length);
                return null;
            }
        }

        /// <summary>
        /// The configured roots, longest first, for trimming paths down to what is worth
        /// reading in a table cell.
        /// </summary>
        private async Task<IReadOnlyList<string>> ResolveLibraryRootsAsync()
        {
            try
            {
                var roots = await rootFolderService.GetAllAsync();
                return [.. roots
                    .Select(root => root.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!.TrimEnd('/', '\\'))
                    .OrderByDescending(path => path.Length)];
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Could not read the root folders for the library tag table");
                return [];
            }
        }

        /// <summary>
        /// A path with its root folder trimmed off, for display only.
        /// </summary>
        /// <remarks>
        /// Presentation, not identity: comparing case-insensitively here would be wrong
        /// for deciding whether two paths are the same file, but this only decides how
        /// much of a string to show, and a root stored as <c>/Media</c> against a path
        /// recorded as <c>/media/…</c> should still read as a library path. Nothing is
        /// trimmed when no root matches, so an unrecognised path stays complete rather
        /// than being silently shortened into something ambiguous.
        /// </remarks>
        private static string? ToLibraryRelativePath(string? path, IReadOnlyList<string> roots)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            foreach (var root in roots)
            {
                // A folder that is the root itself trims to nothing, which is the honest
                // answer for a file sitting directly in the library root — showing the
                // absolute root there would be the one row in the column that did.
                if (path.Length == root.Length
                    && path.Equals(root, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                if (path.Length > root.Length
                    && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && (path[root.Length] == '/' || path[root.Length] == '\\'))
                {
                    return path[(root.Length + 1)..];
                }
            }

            return path;
        }
    }
}
