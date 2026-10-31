using Listenarr.Domain.Audiobooks.Enumerations;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.FileSystem;

public partial class FileMover
{
    public async Task<bool> CompletePreparedMoveAsync(
        string source,
        string destination,
        IAudiobookFileRegistrationLease registrationLease,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(registrationLease);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (operationId == Guid.Empty)
        {
            LogMutation(
                FileMutationOutcome.Blocked,
                FileAction.Move,
                source,
                destination,
                "A durable prepared move requires a non-empty operation ID");
            return false;
        }

        try
        {
            var markerlessResult = await TryCompletePreparedMoveMarkerlessAsync(
                source,
                destination,
                registrationLease,
                operationId);
            if (markerlessResult.HasValue)
            {
                return markerlessResult.Value;
            }

            LogMutation(
                FileMutationOutcome.Blocked,
                FileAction.Move,
                source,
                destination,
                "Durable markerless registration state is unavailable");
            return false;
        }
        catch (Exception exception) when (exception is not (
            OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            // The publication may already be the library's. Registration commits before
            // the source is retired, so a failure after that point has not lost the
            // file - it has left a durable journal row for the recovery service to
            // finish. Reporting it as a failed move told the caller the opposite: the
            // import said 0 of 1 succeeded while the file sat correctly named and
            // registered in the library, and the only way to find out was to read the
            // journal by hand.
            if (await PublicationAlreadyCommittedAsync(operationId))
            {
                _logger.LogWarning(
                    exception,
                    "Prepared move completed its registration but could not retire its source: "
                        + "{Source} -> {Destination}. The library already owns the file; "
                        + "source cleanup is left to registration recovery",
                    LogRedaction.SanitizeFilePath(source),
                    LogRedaction.SanitizeFilePath(destination));
                return true;
            }

            _logger.LogWarning(
                exception,
                "Prepared move completion failed: {Source} -> {Destination}",
                LogRedaction.SanitizeFilePath(source),
                LogRedaction.SanitizeFilePath(destination));
            return false;
        }
    }

    /// <summary>
    /// Whether a journal state means the library already owns the destination.
    ///
    /// Registration commits before the source is retired, so every state from
    /// <see cref="FileMutationJournalState.RegistrationCommitted"/> onwards describes a
    /// file the audiobook already holds. <see cref="FileMutationJournalState.NeedsAttention"/>
    /// is deliberately not among them: a parked row is one nobody has judged yet, and it
    /// may have been parked before the registration committed.
    /// </summary>
    internal static bool PublicationIsCommitted(FileMutationJournalState? state) =>
        state is FileMutationJournalState.RegistrationCommitted
            or FileMutationJournalState.SourceDeletionAuthorized
            or FileMutationJournalState.SourceDeleted
            or FileMutationJournalState.Completed
            or FileMutationJournalState.OwnerMetadataReconciled;

    /// <summary>
    /// Whether this operation's journal says the library already owns the destination.
    ///
    /// Everything from <see cref="FileMutationJournalState.RegistrationCommitted"/>
    /// onwards is past the point of no return: the file is registered to the audiobook
    /// and only the source's retirement is outstanding. A journal that cannot be read
    /// answers no, because a move that may not have happened must never be reported as
    /// one that did.
    /// </summary>
    private async Task<bool> PublicationAlreadyCommittedAsync(Guid operationId)
    {
        if (_fileMutationJournalStore == null)
        {
            return false;
        }

        try
        {
            var journal = await _fileMutationJournalStore.GetAsync(operationId, CancellationToken.None);
            return PublicationIsCommitted(journal?.State);
        }
        catch (Exception exception) when (exception is not (
            OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            _logger.LogWarning(
                exception,
                "Could not read the move journal for operation {OperationId} while classifying a completion failure",
                operationId);
            return false;
        }
    }
}
