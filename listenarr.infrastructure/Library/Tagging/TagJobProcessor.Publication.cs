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

            var removal = await TryRemoveOriginalAsync(destinationPath, services);
            if (!removal.Removed)
            {
                // Nothing was removed, so the library is intact and the rewrite is only
                // taking up space. Clearing the row first keeps the sweeper free to
                // reclaim it.
                await queue.ClearPendingPublicationAsync(job.Id, CancellationToken.None);
                TryDeleteScratch(scratchPath);
                return PublicationOutcome.Failed(TagWriteFailureKind.Transient, removal.Error!);
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
                    return await HoldOrRestoreAsync(
                        job, scratchPath, destinationPath, services, queue,
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
                    return await HoldOrRestoreAsync(
                        job, scratchPath, destinationPath, services, queue,
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
                    return await HoldOrRestoreAsync(
                        job, scratchPath, destinationPath, services, queue,
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

                return await HoldOrRestoreAsync(
                    job, scratchPath, destinationPath, services, queue,
                    $"The rewritten file could not be published into the library: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle a publication failure without ever leaving the book without a file.
        ///
        /// <para>
        /// The original is already gone, so the held rewrite is the book's only copy.
        /// While attempts remain this holds it and lets the queue retry on its backoff:
        /// the usual causes — a share that blinked, a lock that has since gone — clear
        /// themselves within a retry or two.
        /// </para>
        /// <para>
        /// On the last attempt it stops waiting and puts the file back itself. A host
        /// where publication cannot succeed is not hypothetical — it is every macOS dev
        /// machine, where a bind mount reports two mount IDs for one inode and the
        /// pinned-parent check rejects every written file — and on one of those, holding
        /// and retrying means a book stays short a file until a human reads a log.
        /// </para>
        /// </summary>
        private async Task<PublicationOutcome> HoldOrRestoreAsync(
            TagJob job,
            string scratchPath,
            string destinationPath,
            IServiceProvider services,
            ITagQueueService queue,
            string reason)
        {
            var roots = await GetRootPathsAsync(services);

            // Whatever publication left at the destination is ours: this job removed what
            // was there. Clearing it matters beyond tidiness — the publication stack
            // refuses a destination whose content differs from the source, so a staged
            // remnant would make every later attempt fail for a second, confusing reason.
            DiscardFailedDestination(destinationPath, roots);

            if (job.AttemptCount < job.MaxAttempts)
            {
                logger.LogError(
                    "Tag write {JobId} is holding the only copy of a library file at {Path}: {Reason}",
                    job.Id,
                    LogRedaction.SanitizeFilePath(scratchPath),
                    reason);

                return PublicationOutcome.Failed(
                    TagWriteFailureKind.Transient,
                    $"{reason} The rewritten file is being held and has not been deleted; retrying puts it back.");
            }

            if (!TryRestoreHeldFile(scratchPath, destinationPath, roots))
            {
                logger.LogError(
                    "Tag write {JobId} could not put the held file back at {Path}. It remains at {Held} and will not be removed.",
                    job.Id,
                    LogRedaction.SanitizeFilePath(destinationPath),
                    LogRedaction.SanitizeFilePath(scratchPath));

                return PublicationOutcome.Failed(
                    TagWriteFailureKind.OutputRejected,
                    $"{reason} The rewritten file could not be put back either; it is being held and has not been deleted. Check the log for its location.");
            }

            await queue.ClearPendingPublicationAsync(job.Id, CancellationToken.None);
            TryDeleteScratch(scratchPath);

            logger.LogWarning(
                "Tag write {JobId} could not register its rewrite, so the tagged file was put back at {Path} directly. The library's record of that file is now stale until the book is rescanned.",
                job.Id,
                LogRedaction.SanitizeFilePath(destinationPath));

            return PublicationOutcome.Failed(
                TagWriteFailureKind.OutputRejected,
                $"{reason} The tags were written and the file was put back, but the library could not record it — rescan this book so it picks the file up again.");
        }

        /// <summary>
        /// Put the held rewrite back where the original was.
        ///
        /// A direct write rather than a publication, which is the whole reason it is a
        /// last resort: nothing proves afterwards that the file at that path is the file
        /// we created, and the book's stored physical identity is stale until a scan
        /// reconciles it. That is a worse library record than a publication would leave,
        /// and a far better one than no file at all — and what goes back carries the
        /// original's audio, chapters and cover, because it is a copy of it.
        /// </summary>
        private bool TryRestoreHeldFile(
            string scratchPath,
            string destinationPath,
            IReadOnlyList<string> roots)
        {
            try
            {
                if (!File.Exists(scratchPath))
                {
                    return false;
                }

                if (!TryResolveLibraryTarget(destinationPath, roots, out var safePath))
                {
                    return false;
                }

                File.Copy(scratchPath, safePath, overwrite: true);

                // Proof enough for a fallback: a short copy is the failure worth catching,
                // and the bytes were verified before they were ever written.
                return new FileInfo(safePath).Length == new FileInfo(scratchPath).Length;
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogWarning(
                    ex,
                    "Could not put the held file back at {Path}",
                    LogRedaction.SanitizeFilePath(destinationPath));
                return false;
            }
        }

        /// <summary>
        /// Remove what a failed publication left at the destination. Only ever called
        /// after this job removed the original, so anything there now is this job's.
        /// </summary>
        private void DiscardFailedDestination(string destinationPath, IReadOnlyList<string> roots)
        {
            try
            {
                if (TryResolveLibraryTarget(destinationPath, roots, out var safePath)
                    && File.Exists(safePath))
                {
                    File.Delete(safePath);
                }
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogWarning(
                    ex,
                    "Could not remove the unpublished destination {Path}",
                    LogRedaction.SanitizeFilePath(destinationPath));
            }
        }

        /// <summary>
        /// Remove the file the rewrite replaces, having first proved it is inside a
        /// configured root folder. A path that has drifted outside the library is not
        /// ours to delete, whatever the database says.
        /// </summary>
        private async Task<(bool Removed, string? Error)> TryRemoveOriginalAsync(
            string destinationPath,
            IServiceProvider services)
        {
            try
            {
                var roots = await GetRootPathsAsync(services);
                if (!TryResolveLibraryTarget(destinationPath, roots, out var safePath))
                {
                    return (
                        false,
                        "The file to be replaced is not inside a configured root folder, so it was left alone.");
                }

                services.GetRequiredService<IFileSystem>().DeleteFile(safePath);
                return (true, null);
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogWarning(
                    ex,
                    "Could not remove {Path} to make room for its rewrite",
                    LogRedaction.SanitizeFilePath(destinationPath));
                return (false, $"The file being replaced could not be removed: {ex.Message}");
            }
        }

        private static async Task<IReadOnlyList<string>> GetRootPathsAsync(IServiceProvider services)
        {
            var roots = await services.GetRequiredService<IRootFolderService>().GetAllAsync();
            return [.. roots
                .Select(root => root.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)];
        }

        /// <summary>
        /// Resolve a library path this job may write to, proving first that it is inside a
        /// configured root folder. A path that has drifted outside the library is not
        /// ours to touch, whatever the database says.
        /// </summary>
        private bool TryResolveLibraryTarget(
            string destinationPath,
            IReadOnlyList<string> roots,
            out string safePath)
        {
            safePath = destinationPath;

            if (roots.Count == 0)
            {
                logger.LogWarning(
                    "Refused to touch {Path}: no configured root folder contains it",
                    LogRedaction.SanitizeFilePath(destinationPath));
                return false;
            }

            if (!FileSystemSafety.TryValidateMutationTarget(
                    destinationPath,
                    roots,
                    out safePath,
                    out var reason))
            {
                logger.LogWarning(
                    "Refused to touch {Path}: {Reason}",
                    LogRedaction.SanitizeFilePath(destinationPath),
                    LogRedaction.SanitizeText(reason));
                return false;
            }

            return true;
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
