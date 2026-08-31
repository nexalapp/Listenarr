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
    /// Builds the before-and-after an operator sees before approving a tag write.
    ///
    /// Reads each file's real tags and runs them through the same planner the worker
    /// uses. Nothing is written and nothing is queued: a preview that had a side effect
    /// would be a strange thing to offer as a way of deciding whether to have one.
    /// </summary>
    public sealed class TagPreviewService(
        IAudiobookRepository audiobookRepository,
        IConfigurationService configurationService,
        IAudiobookTagWriter tagWriter,
        AudiobookTagPlanner planner,
        IFileSystem fileSystem,
        ILogger<TagPreviewService> logger) : ITagPreviewService
    {
        public async Task<TagPreview> BuildAsync(
            int audiobookId,
            IReadOnlyCollection<string>? selectedTags = null,
            CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new TagPreview(
                    audiobookId,
                    null,
                    CanWrite: false,
                    [],
                    "That audiobook no longer exists.");
            }

            var files = (audiobook.Files ?? [])
                .Where(file => TaggableFile.IsTaggable(file.Path))
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .ToList();

            if (files.Count == 0)
            {
                return new TagPreview(
                    audiobookId,
                    audiobook.Title,
                    CanWrite: false,
                    [],
                    "This book has no M4B files to write tags into.");
            }

            if (!await tagWriter.IsAvailableAsync(cancellationToken))
            {
                return new TagPreview(
                    audiobookId,
                    audiobook.Title,
                    CanWrite: false,
                    [],
                    "No ffmpeg is installed, so tags cannot be written.");
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            var mappings = TagCatalog.Reconcile(settings.TagMappings);
            var metadata = audiobook.CreateBasicAudioMetadata();
            var selection = selectedTags == null
                ? null
                : new HashSet<string>(selectedTags, StringComparer.OrdinalIgnoreCase);

            var previews = new List<TagPreviewFile>(files.Count);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = AudiobookFilePaths.ResolveFullPath(audiobook, file);
                var name = Path.GetFileName(fullPath ?? file.Path ?? string.Empty);

                if (fullPath == null || !fileSystem.FileExists(fullPath))
                {
                    previews.Add(new TagPreviewFile(
                        file.Id,
                        name,
                        [],
                        "This file is not readable from here, so its current tags are unknown."));
                    continue;
                }

                AudiobookFileTags existing;
                try
                {
                    existing = await tagWriter.ReadAsync(fullPath, cancellationToken);
                }
                catch (Exception ex) when (
                    ex is not OperationCanceledException
                    && ex is not OutOfMemoryException
                    && ex is not StackOverflowException)
                {
                    logger.LogWarning(
                        ex,
                        "Could not read the current tags of {Path} for a preview",
                        LogRedaction.SanitizeFilePath(fullPath));

                    previews.Add(new TagPreviewFile(
                        file.Id,
                        name,
                        [],
                        $"This file's current tags could not be read: {ex.Message}"));
                    continue;
                }

                var plan = planner.Plan(metadata, mappings, existing.Tags, selection);
                previews.Add(new TagPreviewFile(file.Id, name, plan.Changes));
            }

            return new TagPreview(
                audiobookId,
                audiobook.Title,
                CanWrite: previews.Any(preview => preview.Error == null),
                previews);
        }
    }
}
