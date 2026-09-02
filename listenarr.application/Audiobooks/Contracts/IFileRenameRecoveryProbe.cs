namespace Listenarr.Application.Audiobooks.Contracts;

public enum RenameRecoveryRepairOutcome
{
    /// <summary>The journal was completed and the audiobook is no longer blocked.</summary>
    Repaired,

    /// <summary>Nothing was blocking this audiobook.</summary>
    NothingToRepair,

    /// <summary>
    /// The destination is not the file the journal says moved, so completing it would
    /// point the registration at something the library never verified.
    /// </summary>
    EvidenceMissing
}

public sealed record RenameRecoveryRepairResult(
    RenameRecoveryRepairOutcome Outcome,
    string? Detail = null);

public interface IFileRenameRecoveryProbe
{
    Task<bool> HasBlockingAsync(
        int audiobookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt the operator-requested repair of whatever is blocking this audiobook.
    ///
    /// <para>
    /// Reports what happened rather than just whether it worked, because "there was
    /// nothing to repair" and "the destination is not the file that moved" call for
    /// different things from whoever asked.
    /// </para>
    /// </summary>
    Task<RenameRecoveryRepairResult> RepairAsync(
        int audiobookId,
        CancellationToken cancellationToken = default);
}
