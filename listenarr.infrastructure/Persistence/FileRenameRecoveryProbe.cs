using Listenarr.Domain.Audiobooks.Enumerations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Listenarr.Infrastructure.Persistence;

public sealed class FileRenameRecoveryProbe(
    IDbContextFactory<ListenArrDbContext> dbContextFactory,
    IServiceScopeFactory scopeFactory) :
    IFileRenameRecoveryProbe
{
    public async Task<bool> HasBlockingAsync(
        int audiobookId,
        CancellationToken cancellationToken = default)
    {
        if (audiobookId <= 0)
        {
            return false;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.FileMutationJournals
            .AsNoTracking()
            .AnyAsync(journal =>
                journal.AudiobookId == audiobookId
                && journal.AudiobookFileId != null
                && (journal.AudiobookFileId == FileMutationOwner.CompanionFile
                    || journal.AudiobookFileId
                        == FileMutationOwner.RegistrationCompanionFile
                    ? journal.State != FileMutationJournalState.Completed
                    : journal.State != FileMutationJournalState.OwnerMetadataReconciled),
                cancellationToken);
    }

    public async Task<RenameRecoveryRepairResult> RepairAsync(
        int audiobookId,
        CancellationToken cancellationToken = default)
    {
        if (audiobookId <= 0)
        {
            return new RenameRecoveryRepairResult(
                RenameRecoveryRepairOutcome.NothingToRepair);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var parked = await db.FileMutationJournals
            .AsNoTracking()
            .Where(journal =>
                journal.AudiobookId == audiobookId
                && journal.AudiobookFileId != null
                && journal.Action == FileAction.Move
                && journal.State == FileMutationJournalState.NeedsAttention)
            .OrderBy(journal => journal.CreatedAt)
            .ToListAsync(cancellationToken);
        if (parked.Count == 0)
        {
            return new RenameRecoveryRepairResult(
                RenameRecoveryRepairOutcome.NothingToRepair);
        }

        // The mover is scoped and this probe is not, so it is resolved per repair rather
        // than held. A repair is an operator action, not a hot path.
        using var scope = scopeFactory.CreateScope();
        var fileMover = scope.ServiceProvider.GetRequiredService<IFileMover>();

        foreach (var journal in parked)
        {
            if (string.IsNullOrWhiteSpace(journal.SourcePhysicalObjectIdentity)
                || !await fileMover.TryRepairParkedMoveAsync(
                    journal.DestinationPath,
                    journal.SourcePhysicalObjectIdentity,
                    journal.OperationId,
                    audiobookId,
                    cancellationToken))
            {
                return new RenameRecoveryRepairResult(
                    RenameRecoveryRepairOutcome.EvidenceMissing,
                    $"The file recorded at {journal.DestinationPath} is not the one this "
                        + "organize moved, so completing it would point the library at "
                        + "something it never verified.");
            }
        }

        return new RenameRecoveryRepairResult(RenameRecoveryRepairOutcome.Repaired);
    }
}
