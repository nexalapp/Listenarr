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
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// The library's tag table: every audio file, what it carries, and what Listenarr
    /// would write into it.
    ///
    /// <para>
    /// Built from the files themselves rather than from the database. A table assembled
    /// out of the audiobook records would agree with Listenarr by construction and could
    /// never show the one thing it exists to show — a file whose tags do not say what
    /// Listenarr thinks they say.
    /// </para>
    /// <para>
    /// Every audio file is listed, not only the M4Bs a write can touch. Which books are
    /// still MP3, and therefore still carry no description a player will read, is exactly
    /// the question this table gets opened to answer.
    /// </para>
    /// </summary>
    public sealed class LibraryTagIndexService(
        IAudiobookRepository audiobookRepository,
        IConfigurationService configurationService,
        IAudiobookTagWriter tagWriter,
        AudiobookTagPlanner planner,
        IFileSystem fileSystem,
        LibraryTagCache cache,
        ILogger<LibraryTagIndexService> logger) : ILibraryTagIndexService
    {
        /// <summary>
        /// How many files are probed at once on a cold load.
        ///
        /// Each probe is a process against a file that may live on a spinning NAS disk,
        /// so this trades a first load of seconds rather than a minute against not
        /// flooding the array with seeks. It is not a throughput knob worth tuning.
        /// </summary>
        private const int ProbeConcurrency = 4;

        public async Task<LibraryTagIndex> BuildAsync(
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            if (refresh)
            {
                cache.Clear();
            }

            var audiobooks = await audiobookRepository.GetLibraryAsync();
            var memberships =
                await audiobookRepository.GetAllSeriesMembershipsGroupedByAudiobookIdAsync(
                    cancellationToken);

            var settings = await configurationService.GetApplicationSettingsAsync();
            var mappings = TagCatalog.Reconcile(settings.TagMappings);

            var probeAvailable = await tagWriter.IsAvailableAsync(cancellationToken);
            var filesRead = 0;

            // The library query loads files but not memberships, and the album tag's
            // bracketed form is built from every series a book belongs to — without them a
            // cross-series book's expected album loses a bracket and every one of them
            // reads as a mismatch. The metadata is built once per book rather than once per
            // file: it does not vary between a book's parts, and rendering it again for
            // each would re-run every pattern for nothing.
            var metadataByAudiobookId = audiobooks.ToDictionary(
                audiobook => audiobook.Id,
                audiobook => audiobook.CreateBasicAudioMetadata(
                    memberships.TryGetValue(audiobook.Id, out var bookMemberships)
                        ? bookMemberships
                        : null));

            var work = new List<(Audiobook Book, AudiobookFile File, string? FullPath)>();
            foreach (var audiobook in audiobooks)
            {
                foreach (var file in (audiobook.Files ?? [])
                    .Where(file => FileUtils.IsAudioFile(file.Path ?? string.Empty))
                    .OrderBy(file => file.Path, StringComparer.Ordinal))
                {
                    work.Add((audiobook, file, AudiobookFilePaths.ResolveFullPath(audiobook, file)));
                }
            }

            var rows = new LibraryTagRow[work.Count];
            using var gate = new SemaphoreSlim(ProbeConcurrency);

            var probes = work.Select(async (item, index) =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var (tags, error, read) = await ReadTagsAsync(
                        item.FullPath,
                        probeAvailable,
                        cancellationToken);

                    if (read)
                    {
                        Interlocked.Increment(ref filesRead);
                    }

                    rows[index] = BuildRow(
                        item.Book,
                        metadataByAudiobookId[item.Book.Id],
                        item.File,
                        item.FullPath,
                        mappings,
                        tags,
                        error);
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();

            try
            {
                await Task.WhenAll(probes);
            }
            catch
            {
                // Every probe is awaited before this method returns, cancelled or not.
                //
                // Task.WhenAll rethrows on the first failure while the rest are still
                // running, and `gate` is disposed on the way out — which would pull the
                // semaphore out from under probes still waiting on it, and abandon the
                // ffprobe processes already in flight with nothing left holding a
                // reference to close their pipes. On a library of several hundred files
                // that is a burst of orphaned descriptors per cancelled request, and a
                // page the operator can navigate away from is cancelled often.
                //
                // Waiting for the settled state costs one probe's worth of time and makes
                // the cleanup deterministic.
                await Task.WhenAll(probes.Select(probe =>
                    probe.ContinueWith(
                        _ => { },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default)));
                throw;
            }

            return new LibraryTagIndex(rows, filesRead, DateTime.UtcNow);
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

            var cached = cache.TryGet(fullPath, length, lastWrite);
            if (cached != null)
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

        /// <summary>
        /// One row: the file's real tags beside the planner's, with the disagreements named.
        /// </summary>
        /// <remarks>
        /// A tag counts as mismatched when planning against the file's own tags comes back
        /// as a write. That is deliberately the planner's answer rather than a string
        /// comparison of its own: a tag whose mapping is off, or which the book has no
        /// value for, is not a fault in the file, and flagging it would bury the handful
        /// of rows that are genuinely wrong.
        /// </remarks>
        private LibraryTagRow BuildRow(
            Audiobook audiobook,
            AudioMetadata metadata,
            AudiobookFile file,
            string? fullPath,
            IReadOnlyList<TagMapping> mappings,
            AudiobookFileTags? tags,
            string? error)
        {
            var storedPath = file.Path ?? string.Empty;
            var fileName = Path.GetFileName(fullPath ?? storedPath);
            var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

            var present = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in tags?.Tags ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrWhiteSpace(key)
                    || TagCatalog.ContainerTags.Contains(key)
                    || !TagCatalog.IsKnown(key))
                {
                    continue;
                }

                // Canonical casing, so a file carrying "series" and the catalog's "SERIES"
                // land in one column rather than being read as two different tags.
                var definition = TagCatalog.Find(key);
                if (definition != null && !string.IsNullOrWhiteSpace(value))
                {
                    present.TryAdd(definition.Tag, value);
                }
            }

            var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var mismatched = new List<string>();

            if (error == null)
            {
                var plan = planner.Plan(metadata, mappings, tags?.Tags);

                foreach (var change in plan.Changes)
                {
                    if (!string.IsNullOrWhiteSpace(change.Proposed))
                    {
                        expected[change.Tag] = change.Proposed;
                    }

                    if (change.IsWrite)
                    {
                        mismatched.Add(change.Tag);
                    }
                }
            }

            return new LibraryTagRow(
                audiobook.Id,
                file.Id,
                audiobook.Title ?? string.Empty,
                fileName,
                storedPath,
                extension,
                TaggableFile.IsTaggable(fullPath ?? storedPath),
                present,
                expected,
                mismatched,
                error);
        }
    }
}
