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
        if (journal == null || journal.State >= FileMutationJournalState.Completed)
        {
            return false;
        }

        if (!MatchesAdoptableDestination(
                destination,
                expectedSourcePhysicalObjectIdentity))
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
    /// Whether the destination is the moved file itself, judged on object identity
    /// rather than on name or length. Every failure to prove that is a false.
    /// </summary>
    private static bool MatchesAdoptableDestination(
        string destination,
        string expectedSourcePhysicalObjectIdentity)
    {
        var parentPath = Path.GetDirectoryName(destination);
        var name = Path.GetFileName(destination);
        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            using var parent = PinnedDirectoryCreation.OpenPinnedDirectoryNoFollow(parentPath);
            using var target = parent.TryOpenExistingFile(name, requireDeleteAccess: false);
            return target != null
                && target.VisiblePathMatches()
                && target.MatchesObjectIdentity(expectedSourcePhysicalObjectIdentity);
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
