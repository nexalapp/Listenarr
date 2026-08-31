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
using Listenarr.Application.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Tagging
{
    public sealed partial class TagJobProcessor
    {
        private async Task<ExecutionOutcome> ExecuteAsync(
            TagJob job,
            IServiceProvider services,
            ITagQueueService queue,
            CancellationToken cancellationToken)
        {
            var audiobookRepository = services.GetRequiredService<IAudiobookRepository>();
            var audiobook = await audiobookRepository.GetByIdAsync(job.AudiobookId);
            if (audiobook == null)
            {
                return ExecutionOutcome.Failed(
                    TagWriteFailureKind.SourceUnreadable,
                    "That audiobook no longer exists.");
            }

            // A tag write replaces what the library serves for this book, so it must not
            // run while a move owns the book's filesystem state. Transient by design: the
            // move finishes, and the retry then finds the files where the move left them.
            try
            {
                await services.GetRequiredService<IMoveQueueService>()
                    .EnsureFilesystemMutationAllowedAsync(job.AudiobookId, cancellationToken);
            }
            catch (ApplicationConflictException conflict)
            {
                return ExecutionOutcome.Failed(TagWriteFailureKind.Transient, conflict.SafeDetail);
            }

            var tagsWritten = 0;
            var filesWritten = 0;

            // A previous attempt may have removed a library file and be holding its only
            // replacement. Publishing that comes before anything else: until it lands the
            // book has no file at all, and every further rewrite would be building on a
            // library that is missing one.
            var resumed = await TryResumePendingPublicationAsync(job, audiobook, services, queue, cancellationToken);
            if (resumed != null)
            {
                if (!resumed.Value.Success)
                {
                    return ExecutionOutcome.Failed(resumed.Value.FailureKind, resumed.Value.Error!);
                }

                tagsWritten += resumed.Value.TagsWritten;
                filesWritten++;
            }

            var files = (audiobook.Files ?? [])
                .Where(file => TaggableFile.IsTaggable(file.Path))
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .ToList();

            if (files.Count == 0)
            {
                return filesWritten > 0
                    ? ExecutionOutcome.Succeeded(tagsWritten, filesWritten, audiobook.Title)
                    : ExecutionOutcome.Failed(
                        TagWriteFailureKind.SourceUnreadable,
                        "This book no longer has any M4B files to write tags into.");
            }

            var settings = await services.GetRequiredService<IConfigurationService>()
                .GetApplicationSettingsAsync();
            var mappings = TagCatalog.Reconcile(settings.TagMappings);
            var selection = TagQueueService.DeserializeSelection(job.SelectedTagsJson);

            var planner = services.GetRequiredService<AudiobookTagPlanner>();
            var writer = services.GetRequiredService<IAudiobookTagWriter>();

            var scratchDirectory = services.GetRequiredService<IApplicationPathService>()
                .ResolveFromConfig("tagging");
            Directory.CreateDirectory(scratchDirectory);

            var coverArtPath = settings.EmbedCoverArtInTags
                ? await ResolveCoverArtAsync(audiobook, services)
                : null;

            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = files[index];
                if (job.PendingFileId == file.Id && resumed != null)
                {
                    // Already handled by the resume above.
                    continue;
                }

                var basePercent = 100.0 * index / files.Count;
                await queue.ReportProgressAsync(
                    job.Id,
                    TagJobPhase.Reading,
                    basePercent,
                    cancellationToken);

                var fullPath = AudiobookFilePaths.ResolveFullPath(audiobook, file);
                if (fullPath == null || !File.Exists(fullPath))
                {
                    return ExecutionOutcome.Failed(
                        TagWriteFailureKind.SourceUnreadable,
                        $"File is missing: {LogRedaction.SanitizeFilePath(fullPath ?? file.Path)}");
                }

                AudiobookFileTags existing;
                try
                {
                    existing = await writer.ReadAsync(fullPath, cancellationToken);
                }
                catch (FfmpegException ex)
                {
                    return ExecutionOutcome.Failed(TagWriteFailureKind.SourceUnreadable, ex.Message);
                }

                var metadata = AudiobookTagMetadata.Create(audiobook, existing.Tags);
                var plan = planner.Plan(metadata, mappings, existing.Tags, selection);

                if (!plan.HasChanges)
                {
                    // Nothing to write, so nothing is rewritten and nothing is touched.
                    // This is the ordinary outcome of a second run, and it is what makes
                    // running this on every import cheap rather than reckless: an
                    // already-correct book costs one probe.
                    logger.LogInformation(
                        "Tag write {JobId}: {Path} already carries the mapped tags",
                        job.Id,
                        LogRedaction.SanitizeFilePath(fullPath));
                    continue;
                }

                // Only fill an empty cover; replacing existing art is never automatic,
                // because the file's own art may be the better one and nothing here can
                // tell.
                var cover = existing.HasCoverArt ? null : coverArtPath;

                var scratchPath = Path.Combine(
                    scratchDirectory,
                    $"tagging-{job.Id:N}-{file.Id}.m4b");

                var request = new TagWriteRequest(
                    fullPath,
                    scratchPath,
                    plan.FinalTags,
                    existing,
                    cover);

                await queue.ReportProgressAsync(
                    job.Id,
                    TagJobPhase.Writing,
                    basePercent,
                    cancellationToken);

                TagWriteResult result;
                try
                {
                    result = await writer.WriteAsync(
                        request,
                        BuildProgress(queue, job.Id, basePercent, 100.0 / files.Count),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Nothing has been removed or published, so the scratch file is only
                    // taking up space.
                    TryDeleteScratch(scratchPath);
                    throw;
                }

                if (!result.Success)
                {
                    TryDeleteScratch(scratchPath);
                    return ExecutionOutcome.Failed(
                        result.FailureKind,
                        result.Message ?? "The tag write failed.");
                }

                var publication = await ReplaceLibraryFileAsync(
                    job,
                    audiobook,
                    file,
                    fullPath,
                    scratchPath,
                    services,
                    queue,
                    cancellationToken);

                if (!publication.Success)
                {
                    return ExecutionOutcome.Failed(publication.FailureKind, publication.Error!);
                }

                tagsWritten += plan.Changes.Count(change => change.IsWrite);
                filesWritten++;
            }

            return ExecutionOutcome.Succeeded(tagsWritten, filesWritten, audiobook.Title);
        }

        /// <summary>
        /// Publish a rewrite an earlier attempt produced but could not put back.
        ///
        /// Returns null when there is nothing pending. The library file this replaces has
        /// already been removed, so this runs before any other work and its failure ends
        /// the job: continuing would leave the book short a file while rewriting others.
        /// </summary>
        private async Task<PublicationOutcome?> TryResumePendingPublicationAsync(
            TagJob job,
            Audiobook audiobook,
            IServiceProvider services,
            ITagQueueService queue,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(job.PendingOutputPath)
                || string.IsNullOrWhiteSpace(job.PendingDestinationPath)
                || job.PendingFileId is not { } pendingFileId)
            {
                return null;
            }

            var file = (audiobook.Files ?? []).FirstOrDefault(candidate => candidate.Id == pendingFileId);
            if (file == null)
            {
                // The registration is gone, so there is nothing left to replace. The
                // scratch file is now the only copy of a file the library has forgotten;
                // say so loudly rather than sweeping it.
                logger.LogError(
                    "Tag write {JobId} holds a rewrite for file {FileId}, which the library no longer knows about. It is at {Path} and will not be removed.",
                    job.Id,
                    pendingFileId,
                    LogRedaction.SanitizeFilePath(job.PendingOutputPath));

                return PublicationOutcome.Failed(
                    TagWriteFailureKind.OutputRejected,
                    "A rewritten file is being held for a library entry that no longer exists. It has not been deleted; check the log for its location.");
            }

            if (!File.Exists(job.PendingOutputPath))
            {
                logger.LogError(
                    "Tag write {JobId} recorded a rewrite at {Path} that is no longer there",
                    job.Id,
                    LogRedaction.SanitizeFilePath(job.PendingOutputPath));

                await queue.ClearPendingPublicationAsync(job.Id, CancellationToken.None);
                return PublicationOutcome.Failed(
                    TagWriteFailureKind.OutputRejected,
                    "The rewritten file this job was holding has gone, and the library file it replaced was already removed. Rescan the book's folder.");
            }

            if (job.PendingOutputLength is { } expectedLength
                && new FileInfo(job.PendingOutputPath).Length != expectedLength)
            {
                return PublicationOutcome.Failed(
                    TagWriteFailureKind.OutputRejected,
                    "The rewritten file this job was holding is not the one that was verified. It has not been deleted; check the log for its location.");
            }

            logger.LogInformation(
                "Tag write {JobId} is resuming a held publication for file {FileId}",
                job.Id,
                pendingFileId);

            return await PublishRewriteAsync(
                job,
                audiobook,
                file,
                job.PendingDestinationPath,
                job.PendingOutputPath,
                services,
                queue,
                cancellationToken);
        }

        /// <summary>
        /// Turn per-file ffmpeg progress into progress over the whole job, so a book with
        /// four files does not run its bar from zero to a hundred four times.
        /// </summary>
        private IProgress<double> BuildProgress(
            ITagQueueService queue,
            Guid jobId,
            double basePercent,
            double span)
        {
            var lastReportedPercent = -1;
            return new Progress<double>(fraction =>
            {
                var percent = (int)Math.Round(basePercent + Math.Clamp(fraction, 0, 1) * span);
                if (percent == Interlocked.Exchange(ref lastReportedPercent, percent))
                {
                    return;
                }

                // Fire-and-forget: progress that could not be persisted must never stall
                // or fail the rewrite reporting it. A report can also land after the job
                // has finished and its scope has been disposed, so nothing here may throw
                // into an unobserved task.
                _ = ReportProgressSafelyAsync(queue, jobId, percent);
            });
        }

        private async Task ReportProgressSafelyAsync(ITagQueueService queue, Guid jobId, int percent)
        {
            try
            {
                await queue.ReportProgressAsync(
                    jobId,
                    TagJobPhase.Writing,
                    percent,
                    CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Could not record progress for tag write {JobId}", jobId);
            }
        }

        /// <summary>
        /// A local copy of the book's cover, if one is cached, for embedding into a file
        /// that carries none. Returns null when there is nothing to embed — a missing
        /// cover is not a reason to fail a tag write.
        /// </summary>
        private async Task<string?> ResolveCoverArtAsync(Audiobook audiobook, IServiceProvider services)
        {
            var identifier = audiobook.Asin;
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return null;
            }

            try
            {
                var relative = await services.GetRequiredService<IImageCacheService>()
                    .GetCachedImagePathAsync(identifier);
                if (string.IsNullOrWhiteSpace(relative))
                {
                    return null;
                }

                var contentRoot = services.GetRequiredService<IApplicationPathService>().ContentRootPath;
                var absolute = Path.GetFullPath(Path.Combine(contentRoot, relative));

                // An SVG placeholder is not cover art, and the mov muxer cannot carry it.
                var extension = Path.GetExtension(absolute);
                if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return File.Exists(absolute) ? absolute : null;
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not resolve cached cover art for audiobook {AudiobookId}", audiobook.Id);
                return null;
            }
        }
    }
}
