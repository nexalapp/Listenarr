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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Tagging
{
    public sealed partial class TagJobProcessor
    {
        /// <summary>
        /// Put a verified rewrite where the file it was made from is.
        ///
        /// <para>
        /// The publication stack cannot overwrite: <c>PrepareActionForRegistrationAsync</c>
        /// treats an existing destination as a resumed publication of the <em>same</em>
        /// bytes and refuses when the content differs, which is exactly the case here.
        /// So the original is removed first and the rewrite published into the space it
        /// leaves — through the file mover and the registration lease, so the written file
        /// is still proved to be the file we created by mount ID and inode.
        /// </para>
        /// <para>
        /// Removing a library file before its replacement is in place is only defensible
        /// because of what the replacement is. It is a stream copy: the audio is bit
        /// identical, the chapters and cover art have been read back and counted, and
        /// every tag written has been compared against what came back. Nothing in the
        /// original is absent from it. The job records the rewrite <em>before</em> the
        /// removal, so a crash in the window leaves a durable row naming the only copy,
        /// the sweeper is forbidden from touching a file in that state, and the next
        /// attempt publishes it before doing anything else.
        /// </para>
        /// </summary>
        private async Task<PublicationOutcome> ReplaceLibraryFileAsync(
            TagJob job,
            Audiobook audiobook,
            AudiobookFile file,
            string destinationPath,
            string scratchPath,
            IServiceProvider services,
            ITagQueueService queue,
            CancellationToken cancellationToken)
        {
            await queue.ReportProgressAsync(job.Id, TagJobPhase.Publishing, job.Progress, cancellationToken);

            long scratchLength;
            try
            {
                scratchLength = new FileInfo(scratchPath).Length;
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                return PublicationOutcome.Failed(
                    TagWriteFailureKind.Transient,
                    $"The rewritten file could not be measured before publishing: {ex.Message}");
            }

            // Durable first. If the process dies between here and the removal below, the
            // worst case is a row pointing at a rewrite that was never needed, which the
            // resume path detects and discards. The reverse order risks a removed file
            // that nothing knows how to replace.
            await queue.RecordPendingPublicationAsync(
                job.Id,
                file.Id,
                scratchPath,
                scratchLength,
                destinationPath,
                CancellationToken.None);

            if (!TryRemoveOriginal(destinationPath, services, out var removalError))
            {
                await queue.ClearPendingPublicationAsync(job.Id, CancellationToken.None);
                TryDeleteScratch(scratchPath);
                return PublicationOutcome.Failed(TagWriteFailureKind.Transient, removalError!);
            }

            return await PublishRewriteAsync(
                job,
                audiobook,
                file,
                destinationPath,
                scratchPath,
                services,
                queue,
                cancellationToken);
        }

        /// <summary>
        /// Move a verified rewrite into the library and re-register it as the file's new
        /// physical generation. The destination is expected to be free: either this run
        /// just removed the original, or an earlier attempt did.
        /// </summary>
        private async Task<PublicationOutcome> PublishRewriteAsync(
            TagJob job,
            Audiobook audiobook,
            AudiobookFile file,
            string destinationPath,
            string scratchPath,
            IServiceProvider services,
            ITagQueueService queue,
            CancellationToken cancellationToken)
        {
            var fileMover = services.GetRequiredService<IFileMover>();
            var audiobookFileService = services.GetRequiredService<IAudiobookFileService>();

            try
            {
                // Stable per (job, file, destination) so a retry after a crash resumes the
                // same durable operation rather than starting a second one against a
                // journal row that already names this scratch path.
                var operationId = BuildOperationId(job.Id, file.Id, destinationPath);

                using var registrationLease = await fileMover.PrepareActionForRegistrationAsync(
                    FileAction.Move,
                    scratchPath,
                    destinationPath,
                    operationId);

                if (registrationLease == null)
                {
                    return HoldForRetry(
                        job,
                        scratchPath,
                        "The rewritten file could not be published into the library.");
                }

                var ownership = await audiobookFileService.CheckAudiobookFileOwnershipAsync(
                    audiobook,
                    destinationPath,
                    Path.GetDirectoryName(destinationPath),
                    cancellationToken);

                // The book already owns this path, so registration takes the
                // already-owned branch and refreshes the physical generation rather than
                // claiming a new row. That is the same path a scan uses when it finds a
                // file whose bytes have changed underneath it.
                var registered = await audiobookFileService.RegisterPublishedGenerationAsync(
                    audiobook,
                    ownership,
                    registrationLease,
                    "tagging",
                    cancellationToken);

                if (!registered)
                {
                    return HoldForRetry(
                        job,
                        scratchPath,
                        "The rewritten file could not be registered in the library.");
                }

                // The move is staged as a copy until registration is durable; only now is
                // the scratch file retired.
                if (!await fileMover.CompletePreparedMoveAsync(
                        scratchPath,
                        destinationPath,
                        registrationLease,
                        operationId))
                {
                    await audiobookFileService.RollbackPublishedGenerationIfStaleAsync(
                        audiobook,
                        registrationLease);
                    return HoldForRetry(
                        job,
                        scratchPath,
                        "The rewritten file could not be committed into the library.");
                }

                var completion = registrationLease.CompletePublication();
                if (completion == RegistrationPublicationCompletion.CommittedCleanupPending)
                {
                    logger.LogWarning(
                        "Tag write committed for audiobook {AudiobookId} with registration cleanup still pending for {Destination}",
                        audiobook.Id,
                        LogRedaction.SanitizeFilePath(destinationPath));
                }

                await queue.ClearPendingPublicationAsync(job.Id, CancellationToken.None);
                return PublicationOutcome.Published();
            }
            catch (OperationCanceledException)
            {
                // The scratch file stays: the library file it replaces is gone, and the
                // pending-publication row is what the next attempt reads.
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(
                    ex,
                    "Publishing the rewritten file for audiobook {AudiobookId} was rejected",
                    audiobook.Id);

                return HoldForRetry(
                    job,
                    scratchPath,
                    $"The rewritten file could not be published into the library: {ex.Message}");
            }
        }

        /// <summary>
        /// Report a publication failure without discarding the rewrite.
        ///
        /// The original is already gone, so the held file is the book's only copy. The
        /// message has to be actionable on its own: an operator reading Activity needs to
        /// know the book is missing a file and that retrying is what puts it back.
        /// </summary>
        private PublicationOutcome HoldForRetry(TagJob job, string scratchPath, string reason)
        {
            logger.LogError(
                "Tag write {JobId} is holding the only copy of a library file at {Path}: {Reason}",
                job.Id,
                LogRedaction.SanitizeFilePath(scratchPath),
                reason);

            return PublicationOutcome.Failed(
                TagWriteFailureKind.OutputRejected,
                $"{reason} The rewritten file is being held and has not been deleted; retry this job to put it back.");
        }

        /// <summary>
        /// Remove the file the rewrite replaces, having first proved it is inside a
        /// configured root folder. A path that has drifted outside the library is not
        /// ours to delete, whatever the database says.
        /// </summary>
        private bool TryRemoveOriginal(
            string destinationPath,
            IServiceProvider services,
            out string? error)
        {
            try
            {
                var roots = services.GetRequiredService<IRootFolderService>()
                    .GetAllAsync()
                    .GetAwaiter()
                    .GetResult()
                    .Select(root => root.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToList();

                var reason = "no configured root folder contains it";
                if (roots.Count == 0
                    || !FileSystemSafety.TryValidateMutationTarget(
                        destinationPath,
                        roots,
                        out var safePath,
                        out reason))
                {
                    error = "The file to be replaced is not inside a configured root folder, so it was left alone.";
                    logger.LogWarning(
                        "Refused to replace {Path}: {Reason}",
                        LogRedaction.SanitizeFilePath(destinationPath),
                        LogRedaction.SanitizeText(reason));
                    return false;
                }

                services.GetRequiredService<IFileSystem>().DeleteFile(safePath);
                error = null;
                return true;
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogWarning(
                    ex,
                    "Could not remove {Path} to make room for its rewrite",
                    LogRedaction.SanitizeFilePath(destinationPath));
                error = $"The file being replaced could not be removed: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// A stable operation ID for one job's publication of one file, so a retry of
        /// <em>that job</em> resumes the same durable operation after a crash instead of
        /// starting another. Keyed on the job as well as the file, because the journal
        /// records this operation's source path — the job's own scratch file — and a key
        /// shared across jobs would make a later run collide with an earlier one's row.
        /// </summary>
        internal static Guid BuildOperationId(Guid jobId, int fileId, string destinationPath)
        {
            var seed = $"tagging:{jobId:N}:{fileId}:{FileUtils.NormalizeStoredPath(destinationPath)}";
            var hash = System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(seed));
            return new Guid(hash);
        }

        private readonly record struct PublicationOutcome(
            bool Success,
            int TagsWritten,
            TagWriteFailureKind FailureKind,
            string? Error)
        {
            public static PublicationOutcome Published(int tagsWritten = 0) =>
                new(true, tagsWritten, TagWriteFailureKind.None, null);

            public static PublicationOutcome Failed(TagWriteFailureKind kind, string error) =>
                new(false, 0, kind, error);
        }
    }
}
