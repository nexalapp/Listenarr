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

namespace Listenarr.Infrastructure.FileSystem;

public partial class FileMover
{
    /// <summary>
    /// Adopt a move that already happened.
    ///
    /// <para>
    /// A resume whose source has gone is ambiguous on its face, and the reconciler used
    /// to read it one way only: unresumable, park the journal for a human. But the
    /// commonest reason a source is missing is that the rename landed and the process
    /// died before the row could say so, and parking that case strands a book whose
    /// files are in perfectly good order.
    /// </para>
    /// <para>
    /// A move preserves the object identity, so a destination carrying the identity the
    /// journal recorded for its source is that same file, arrived. Nothing weaker is
    /// accepted: a destination that is missing, unreadable, or some other file leaves
    /// the journal exactly as it was.
    /// </para>
    /// </summary>
    public async Task<bool> TryAdoptCompletedMoveAsync(
        string destination,
        string expectedSourcePhysicalObjectIdentity,
        Guid operationId,
        int audiobookId,
        int audiobookFileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination)
            || string.IsNullOrWhiteSpace(expectedSourcePhysicalObjectIdentity))
        {
            return false;
        }

        if (_fileMutationJournalStore == null)
        {
            return false;
        }

        var journal = await _fileMutationJournalStore.GetAsync(operationId, cancellationToken);
        // Not a comparison: NeedsAttention sorts after Completed but is exactly the
        // state this exists to rescue. Only a move that genuinely finished is off limits.
        if (journal == null
            || journal.State is FileMutationJournalState.Completed
                or FileMutationJournalState.OwnerMetadataReconciled)
        {
            return false;
        }

        if (!MatchesAdoptableDestination(destination, journal))
        {
            return false;
        }

        // The identity is the source's, because that is what a move carries across. It
        // becomes the target identity because the file it names now lives at the target.
        await _fileMutationJournalStore.AdvanceAsync(
            operationId,
            FileMutationJournalState.Completed,
            expectedSourcePhysicalObjectIdentity,
            audiobookId,
            error: null,
            cancellationToken);

        _logger.LogInformation(
            "Adopted the completed move recorded by organize journal {OperationId} for audiobook {AudiobookId} file {AudiobookFileId}: "
                + "the destination carries the object identity the journal recorded for its source",
            operationId,
            audiobookId,
            audiobookFileId);

        return true;
    }

    /// <summary>
    /// Repair a parked move at an operator's request.
    ///
    /// <para>
    /// Same evidence as the automatic adoption, and the same refusal when it is absent -
    /// asking for a repair does not make a destination into the file that was moved. What
    /// the operator's involvement changes is only that a parked journal may be completed
    /// at all, which no background pass is allowed to decide.
    /// </para>
    /// </summary>
    public async Task<bool> TryRepairParkedMoveAsync(
        string destination,
        string expectedSourcePhysicalObjectIdentity,
        Guid operationId,
        int audiobookId,
        CancellationToken cancellationToken = default)
    {
        if (_fileMutationJournalStore == null
            || string.IsNullOrWhiteSpace(destination)
            || string.IsNullOrWhiteSpace(expectedSourcePhysicalObjectIdentity))
        {
            return false;
        }

        var journal = await _fileMutationJournalStore.GetAsync(operationId, cancellationToken);
        if (journal == null
            || journal.State != FileMutationJournalState.NeedsAttention)
        {
            return false;
        }

        if (!MatchesAdoptableDestination(destination, journal))
        {
            return false;
        }

        await _fileMutationJournalStore.RepairParkedAsync(
            operationId,
            expectedSourcePhysicalObjectIdentity,
            audiobookId,
            cancellationToken);

        _logger.LogInformation(
            "Repaired parked organize journal {OperationId} for audiobook {AudiobookId} at an operator's request: "
                + "the destination holds the bytes the journal recorded for its source",
            operationId,
            audiobookId);

        return true;
    }

    /// <summary>
    /// Whether the destination holds the file that was being moved, judged on its bytes.
    /// Every failure to prove that is a false.
    /// </summary>
    /// <remarks>
    /// This asks a different question from "which file is at this path" - it asks whether
    /// the work already happened - and a path cannot answer it. Anything may occupy a
    /// name: an older copy of the same book, a colliding edition, a delivery from a sync
    /// client sharing the folder. Answering "yes" for any of them marks an organize
    /// complete that never ran, and lets its source be retired.
    /// <para>
    /// It used to compare the inode, which was a sound proof - a rename preserves it - but
    /// only a proxy for "the same bytes", and one that pooled FUSE filesystems break by
    /// renumbering an untouched file. The bytes themselves are what the question was
    /// always about, they are already recorded on the journal, and they do not renumber.
    /// </para>
    /// <para>
    /// Note that a size check alone would not catch an interrupted same-volume move:
    /// those go through renameat2 and are atomic, so the destination is either the whole
    /// file or absent, never a partial. The case worth catching is a different complete
    /// file wearing the same name.
    /// </para>
    /// </remarks>
    private static bool MatchesAdoptableDestination(
        string destination,
        FileMutationJournal journal)
    {
        var parentPath = Path.GetDirectoryName(destination);
        var name = Path.GetFileName(destination);
        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // A length is always recorded and is the floor: without it there is nothing to
        // prove adoption against, and guessing is what this method exists to avoid. A
        // hash is not always recorded - a hardlink does not capture one up front, and
        // older journals predate it - so it strengthens the proof when present rather
        // than gating it. Requiring one would refuse legitimate repairs.
        if (journal.SourceLength <= 0)
        {
            return false;
        }

        try
        {
            using var parent = PinnedDirectoryCreation.OpenPinnedDirectoryNoFollow(parentPath);
            using var target = parent.TryOpenExistingFile(name, requireDeleteAccess: false);
            if (target == null || !target.VisiblePathMatches())
            {
                return false;
            }

            using var stream = target.OpenReadStream(bufferSize: 128 * 1024, asynchronous: false);
            if (stream.Length != journal.SourceLength)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(journal.SourceSha256))
            {
                // Length alone. Weaker, but it is the whole of the evidence recorded for
                // this move, and it still rejects the replacement-by-a-different-file
                // case whenever the sizes differ.
                return true;
            }

            stream.Position = 0;
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(stream));
            return string.Equals(hash, journal.SourceSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception)
        {
            // An unreadable destination is not evidence the move completed, and the
            // journal is no worse off for having asked.
            return false;
        }
    }
}
