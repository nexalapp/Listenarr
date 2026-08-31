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
using Listenarr.Domain.Audiobooks.Enumerations;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Conversion
{
    public sealed partial class ConversionJobProcessor
    {
        /// <summary>
        /// Move the verified M4B into the library and register it.
        ///
        /// This goes through <see cref="IFileMover"/> and the registration lease rather
        /// than File.Move: the project verifies that a written file is the one it created
        /// by comparing mount ID and inode, and a conversion that bypassed that would be
        /// the one place in the library where that guarantee does not hold.
        /// </summary>
        private async Task<PublicationOutcome> PublishConvertedFileAsync(
            Audiobook audiobook,
            string scratchPath,
            string destinationPath,
            IFileMover fileMover,
            IAudiobookFileService audiobookFileService,
            CancellationToken cancellationToken)
        {
            // Stable per (job, destination) so a retry after a crash resumes the same
            // durable operation rather than starting a second one.
            var operationId = BuildOperationId(audiobook.Id, destinationPath);

            using var registrationLease = await fileMover.PrepareActionForRegistrationAsync(
                FileAction.Move,
                scratchPath,
                destinationPath,
                operationId);

            if (registrationLease == null)
            {
                return PublicationOutcome.Failed(
                    "The converted file could not be published into the library. The original files have been left alone.");
            }

            var ownership = await audiobookFileService.CheckAudiobookFileOwnershipAsync(
                audiobook,
                destinationPath,
                Path.GetDirectoryName(destinationPath),
                cancellationToken);

            var registered = await audiobookFileService.RegisterPublishedGenerationAsync(
                audiobook,
                ownership,
                registrationLease,
                "conversion",
                cancellationToken);

            if (!registered)
            {
                return PublicationOutcome.Failed(
                    "The converted file could not be registered in the library. The original files have been left alone.");
            }

            // The move is staged as a copy until registration is durable; only now is the
            // scratch file retired.
            if (!await fileMover.CompletePreparedMoveAsync(
                    scratchPath,
                    destinationPath,
                    registrationLease,
                    operationId))
            {
                await audiobookFileService.RollbackPublishedGenerationIfStaleAsync(
                    audiobook,
                    registrationLease);
                return PublicationOutcome.Failed(
                    "The converted file could not be committed into the library. The original files have been left alone.");
            }

            var completion = registrationLease.CompletePublication();
            if (completion == RegistrationPublicationCompletion.CommittedCleanupPending)
            {
                logger.LogWarning(
                    "Conversion committed for audiobook {AudiobookId} with registration cleanup still pending for {Destination}",
                    audiobook.Id,
                    LogRedaction.SanitizeFilePath(destinationPath));
            }

            return PublicationOutcome.Published(destinationPath);
        }

        /// <summary>
        /// Retire the source MP3s once the M4B is registered.
        ///
        /// Every branch runs strictly after publication, so a conversion that failed
        /// anywhere earlier has already returned and the sources are untouched. A failure
        /// here is reported but does not fail the job: the conversion itself succeeded,
        /// and the library is already serving the M4B.
        /// </summary>
        private async Task<string?> RetireSourcesAsync(
            Audiobook audiobook,
            IReadOnlyList<SourceFileReference> sources,
            ApplicationSettings settings,
            IAudiobookFileRepository fileRepository,
            IFileSystem fileSystem,
            CancellationToken cancellationToken)
        {
            // The library record drops the sources in every case: they are no longer what
            // the book is, whatever happens to the bytes.
            foreach (var source in sources)
            {
                await fileRepository.DeleteAsync(source.FileId, cancellationToken);
            }

            switch (settings.ConversionSourceDisposition)
            {
                case ConversionSourceDisposition.Keep:
                    logger.LogInformation(
                        "Left {Count} source file(s) on disk for audiobook {AudiobookId}",
                        sources.Count,
                        audiobook.Id);
                    return null;

                case ConversionSourceDisposition.Delete:
                    return DeleteSources(audiobook, sources, fileSystem);

                case ConversionSourceDisposition.Archive:
                default:
                    return ArchiveSources(audiobook, sources, settings, fileSystem);
            }
        }

        private string? ArchiveSources(
            Audiobook audiobook,
            IReadOnlyList<SourceFileReference> sources,
            ApplicationSettings settings,
            IFileSystem fileSystem)
        {
            var archiveRoot = settings.ConversionArchivePath;
            if (string.IsNullOrWhiteSpace(archiveRoot))
            {
                // Moving files to a location nobody configured is not a safe guess, so
                // the sources stay put and the operator is told why.
                logger.LogWarning(
                    "Audiobook {AudiobookId} converted, but no conversion archive path is configured, so its {Count} source file(s) were left in place",
                    audiobook.Id,
                    sources.Count);
                return "Converted, but the source files were left in place because no archive path is configured.";
            }

            string destinationDirectory;
            try
            {
                // One directory per book so an archive of many books stays navigable and
                // two books cannot collide on a shared filename like "01.mp3".
                var bookFolder = FileUtils.SafeFileName(
                    audiobook.Title is { Length: > 0 } title
                        ? $"{title} [{audiobook.Id}]"
                        : $"audiobook-{audiobook.Id}");

                destinationDirectory = Path.GetFullPath(Path.Combine(archiveRoot, bookFolder));

                if (!FileSystemSafety.TryValidateMutationTarget(
                        destinationDirectory,
                        [archiveRoot],
                        out destinationDirectory,
                        out var reason))
                {
                    logger.LogWarning(
                        "Blocked conversion archive target outside the configured archive root: {Reason}",
                        LogRedaction.SanitizeText(reason));
                    return "Converted, but the source files could not be archived to a safe location.";
                }

                fileSystem.CreateDirectory(destinationDirectory);
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogWarning(ex, "Could not prepare the conversion archive directory");
                return "Converted, but the archive directory could not be created, so the source files were left in place.";
            }

            var moved = 0;
            var failed = 0;
            foreach (var source in sources)
            {
                try
                {
                    var target = Path.Combine(destinationDirectory, Path.GetFileName(source.FullPath));
                    if (File.Exists(target))
                    {
                        // A previous archive of the same book already holds this name.
                        target = Path.Combine(
                            destinationDirectory,
                            $"{Path.GetFileNameWithoutExtension(source.FullPath)}-{Guid.NewGuid():N}{Path.GetExtension(source.FullPath)}");
                    }

                    File.Move(source.FullPath, target);
                    moved++;
                }
                catch (Exception ex) when (IsNonFatal(ex))
                {
                    failed++;
                    logger.LogWarning(
                        ex,
                        "Could not archive source file {Path}",
                        LogRedaction.SanitizeFilePath(source.FullPath));
                }
            }

            logger.LogInformation(
                "Archived {Moved} of {Total} source file(s) for audiobook {AudiobookId}",
                moved,
                sources.Count,
                audiobook.Id);

            return failed > 0
                ? $"Converted, but {failed} of {sources.Count} source file(s) could not be archived and were left in place."
                : null;
        }

        private string? DeleteSources(
            Audiobook audiobook,
            IReadOnlyList<SourceFileReference> sources,
            IFileSystem fileSystem)
        {
            var failed = 0;
            foreach (var source in sources)
            {
                try
                {
                    fileSystem.DeleteFile(source.FullPath);
                }
                catch (Exception ex) when (IsNonFatal(ex))
                {
                    failed++;
                    logger.LogWarning(
                        ex,
                        "Could not delete source file {Path}",
                        LogRedaction.SanitizeFilePath(source.FullPath));
                }
            }

            logger.LogInformation(
                "Deleted {Count} source file(s) for audiobook {AudiobookId}",
                sources.Count - failed,
                audiobook.Id);

            return failed > 0
                ? $"Converted, but {failed} of {sources.Count} source file(s) could not be deleted."
                : null;
        }

        /// <summary>
        /// A stable operation ID for one (audiobook, destination) publication, so a retry
        /// after a crash resumes the same durable operation instead of starting another.
        /// </summary>
        private static Guid BuildOperationId(int audiobookId, string destinationPath)
        {
            var seed = $"conversion:{audiobookId}:{FileUtils.NormalizeStoredPath(destinationPath)}";
            var hash = System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(seed));
            return new Guid(hash);
        }

        private static bool IsNonFatal(Exception ex) =>
            ex is not OperationCanceledException
            && ex is not OutOfMemoryException
            && ex is not StackOverflowException;

        /// <summary>A source file's library identity alongside its resolved path.</summary>
        private sealed record SourceFileReference(int FileId, string FullPath);

        private sealed record PublicationOutcome(bool Success, string? OutputPath, string? Error)
        {
            public static PublicationOutcome Published(string outputPath) =>
                new(true, outputPath, null);

            public static PublicationOutcome Failed(string error) =>
                new(false, null, error);
        }
    }
}
