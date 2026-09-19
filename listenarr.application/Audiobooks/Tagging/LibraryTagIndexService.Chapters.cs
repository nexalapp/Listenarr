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
using Listenarr.Domain.Audiobooks.Chapters;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// The chapter side of the table: recording each file's verdict on its row and
    /// asking for the fix of any flagged file that lacks a current one.
    /// </summary>
    public sealed partial class LibraryTagIndexService
    {
        private string? PlanKey(string fullPath, string? asin, string? transcriptionModel)
        {
            try
            {
                return ChapterPlanKeys.For(fileSystem.GetFileLength(fullPath), fileSystem.GetLastWriteTimeUtc(fullPath), asin, transcriptionModel);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Ask for the fix of every flagged file that lacks a current one, one job per
        /// book. The queue runs them one at a time, which is what keeps a first pass over
        /// a whole library from hammering Audnexus or the CPU.
        /// </summary>
        private async Task QueuePlanningAsync(IReadOnlyList<LibraryTagRow> rows, CancellationToken cancellationToken)
        {
            if (tagQueue == null)
            {
                return;
            }

            foreach (var book in rows.Where(row => row.ChapterPlanPending).GroupBy(row => row.AudiobookId))
            {
                try
                {
                    await tagQueue.EnqueueChapterPlanAsync(book.Key, book.Select(row => row.FileId).ToList(), TagTrigger.Automatic, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Could not queue chapter planning for audiobook {AudiobookId}", book.Key);
                }
            }
        }

        /// <summary>
        /// Record the verdicts on the files themselves, so the books list and the book
        /// page can show them without this table. Failing costs nothing but the badge.
        /// </summary>
        private async Task PersistChapterHealthAsync(
            IReadOnlyCollection<AudiobookFileChapterHealth> verdicts,
            CancellationToken cancellationToken)
        {
            if (fileRepository == null || verdicts.Count == 0)
            {
                return;
            }

            try
            {
                await fileRepository.SetChapterHealthAsync(verdicts, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not record chapter verdicts for {Count} file(s)", verdicts.Count);
            }
        }

        /// <summary>
        /// Write what this call probed back to the durable store. A store that fails
        /// costs the next process a probe per file, not the table.
        /// </summary>
        private async Task PersistPendingAsync(CancellationToken cancellationToken)
        {
            if (cacheStore == null)
            {
                return;
            }

            var pending = cache.TakePending();
            if (pending.Count == 0)
            {
                return;
            }

            try
            {
                await cacheStore.SaveAsync(pending, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not persist {Count} tag cache entries", pending.Count);
            }
        }

        /// <summary>
        /// One file's current tags, from the cache when its size and modification time
        /// still match, and from a probe otherwise.
        /// </summary>
        /// <returns>
        /// The tags, the reason there are none, and whether this call actually probed —
        /// which is what separates a cold load from a warm one in the reported count.
        /// </returns>
        private async Task<(AudiobookFileTags? Tags, string? Error, bool Read)> ReadTagsAsync(
            string? fullPath,
            bool probeAvailable,
            CancellationToken cancellationToken)
        {
            if (fullPath == null || !fileSystem.FileExists(fullPath))
            {
                return (null, "This file is not readable from here, so its tags are unknown.", false);
            }

            long length;
            DateTime lastWrite;
            try
            {
                length = fileSystem.GetFileLength(fullPath);
                lastWrite = fileSystem.GetLastWriteTimeUtc(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (null, $"This file could not be inspected: {ex.Message}", false);
            }

            // A record cached before chapters were kept is a miss for a taggable file:
            // the row would show its chapters as unknown forever otherwise.
            var cached = cache.TryGet(fullPath, length, lastWrite);
            if (cached != null && (cached.Chapters != null || !TaggableFile.IsTaggable(fullPath)))
            {
                return (cached, null, false);
            }

            if (!probeAvailable)
            {
                return (null, "No ffprobe is installed, so this file's tags cannot be read.", false);
            }

            try
            {
                var tags = await tagWriter.ReadAsync(fullPath, cancellationToken);
                cache.Set(fullPath, length, lastWrite, tags);
                return (tags, null, true);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                logger.LogWarning(
                    ex,
                    "Could not read the tags of {Path} for the library tag table",
                    LogRedaction.SanitizeFilePath(fullPath));

                return (null, $"This file's tags could not be read: {ex.Message}", true);
            }
        }
    }
}
