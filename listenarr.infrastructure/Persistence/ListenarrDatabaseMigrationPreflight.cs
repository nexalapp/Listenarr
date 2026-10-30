using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence;

internal static class ListenarrDatabaseMigrationPreflight
{
    internal const string DurableFilesystemRecoveryMigrationId =
        "20260810160602_AddDurableFilesystemRecovery";
    internal const string RootFoldersMigrationId =
        "20260101172733_AddRootFolders";

    public static ListenarrDatabaseMigrationPreflightResult RepairLegacyData(
        ListenArrDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var applied = context.Database.GetAppliedMigrations()
            .ToHashSet(StringComparer.Ordinal);
        var normalizeDefaultRoots =
            applied.Contains(RootFoldersMigrationId)
            && !applied.Contains(DurableFilesystemRecoveryMigrationId);
        if (!normalizeDefaultRoots)
        {
            return default;
        }

        using var transaction = context.Database.BeginTransaction();
        var defaultRootsNormalized = context.Database.ExecuteSqlRaw(
            """
            UPDATE "RootFolders"
            SET "IsDefault" = 0
            WHERE "IsDefault" = 1
              AND "Id" <> (
                  SELECT MIN("Id")
                  FROM "RootFolders"
                  WHERE "IsDefault" = 1
              );
            """);
        transaction.Commit();

        return new ListenarrDatabaseMigrationPreflightResult(defaultRootsNormalized);
    }

    public static ListenarrDatabasePostMigrationRepairResult RepairPostMigrationData(
        ListenArrDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The audit verdict is stored by name, so renaming the enum member orphans every
        // row already holding the old one: EF cannot map the string back and throws on any
        // query that reaches that audiobook, which takes the library page down. Ungated and
        // idempotent - it matches nothing on a database that never saw the old name, and
        // nothing again once it has run.
        var auditVerdictsRenamed = context.Database.ExecuteSqlRaw(
            """
            UPDATE "Audiobooks"
            SET "AudioAuditVerdict" = 'Incomplete'
            WHERE "AudioAuditVerdict" = 'RuntimeMismatch';
            """);

        // Failures recorded before the reason was a column kept the client's status in
        // their metadata and nothing else, so the Activity page says "Failed" for all of
        // them and a person has to open each one. The classifier can read those statuses,
        // so the backlog is given the reason it was missing. Nothing is blocked and nothing
        // is grabbed by this - it only names what already happened.
        var failureReasonsClassified = ClassifyRecordedFailures(context);

        var applied = context.Database.GetAppliedMigrations()
            .ToHashSet(StringComparer.Ordinal);
        if (!applied.Contains(DurableFilesystemRecoveryMigrationId))
        {
            return new ListenarrDatabasePostMigrationRepairResult(
                0,
                auditVerdictsRenamed,
                failureReasonsClassified);
        }

        using var transaction = context.Database.BeginTransaction();
        var moveJobsRepaired = context.Database.ExecuteSqlRaw(
            """
            UPDATE "MoveJobs"
            SET
                "Status" = 'NeedsAttention',
                "Error" = 'This move job was created by a pre-durable released version and cannot be resumed safely after upgrade.',
                "FailureKind" = 'Verification',
                "ActiveDeduplicationKey" = NULL,
                "UpdatedAt" = CURRENT_TIMESTAMP
            WHERE "ExecutionProtocolVersion" = 0
              AND "Status" NOT IN ('Completed', 'Failed')
              AND (
                  "Status" <> 'NeedsAttention'
                  OR "ActiveDeduplicationKey" IS NOT NULL
                  OR "FailureKind" <> 'Verification'
                  OR "Error" IS NULL
              );
            """);
        transaction.Commit();

        return new ListenarrDatabasePostMigrationRepairResult(
            moveJobsRepaired,
            auditVerdictsRenamed,
            failureReasonsClassified);
    }

    /// <summary>
    /// Gives each failure already on record the reason it was recorded without.
    ///
    /// Done in C# rather than SQL so the one classifier decides, here as everywhere: a
    /// CASE over status prefixes would be a second copy of that mapping, written in the
    /// one language where nothing checks it against the first. A failure whose status
    /// means nothing to the classifier is left alone rather than written as Unknown, so a
    /// later build that understands it can still have its say.
    /// </summary>
    internal static int ClassifyRecordedFailures(ListenArrDbContext context)
    {
        var unclassified = context.Downloads
            .Where(download =>
                download.Status == DownloadStatus.Failed &&
                download.FailureReason == null)
            .ToList();
        if (unclassified.Count == 0)
        {
            return 0;
        }

        var classified = 0;
        foreach (var download in unclassified)
        {
            var reason = NzbgetFailureMessageMapper.Classify(
                download.GetMetadataString("ClientFailureReason"));
            if (reason == DownloadFailureReason.Unknown)
            {
                continue;
            }

            download.FailureReason = reason;
            classified++;
        }

        if (classified > 0)
        {
            context.SaveChanges();
        }

        return classified;
    }
}

internal readonly record struct ListenarrDatabaseMigrationPreflightResult(
    int DefaultRootsNormalized);

internal readonly record struct ListenarrDatabasePostMigrationRepairResult(
    int MoveJobsRepaired,
    int AuditVerdictsRenamed,
    int FailureReasonsClassified);
