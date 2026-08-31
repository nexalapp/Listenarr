using Listenarr.Application.Search.NzbKing;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Search.NzbKing;

[Trait("Name", "NzbKingKeepaliveProcessorTests")]
[Trait("Category", "NzbKingTokenBudget")]
public sealed class NzbKingKeepaliveProcessorTests : BaseTests
{
    private static readonly DateTime Origin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // The selection rule is what matters here — which keys the worker decides are at risk.
    // Exercising it through the repository keeps the test off the network, since a real
    // keepalive query would spend a token from a live allowance.
    private static async Task WithLedgerAsync(Func<INzbKingLedgerRepository, ListenArrDbContext, Task> body)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"listenarr-nzbking-keepalive-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 30
        }.ToString();

        var options = new DbContextOptionsBuilder<ListenArrDbContext>().UseSqlite(connectionString).Options;

        try
        {
            await using var db = new ListenArrDbContext(options);
            await db.Database.EnsureCreatedAsync();
            await body(new EfNzbKingLedgerRepository(db), db);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static NzbKingKeyState Key(
        string fingerprint,
        DateTime createdAt,
        DateTime? lastUse = null,
        DateTime? deletedAt = null) => new()
        {
            KeyFingerprint = fingerprint,
            EstimatedBalance = NzbKingTokenPolicy.MaxTokens,
            LastRefillAt = createdAt,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            LastSuccessfulUseAt = lastUse,
            KeyDeletedAt = deletedAt
        };

    [Fact]
    public async Task GetKeysDueForKeepaliveAsync_SelectsAKeyIdleForTheFullThreshold()
    {
        await WithLedgerAsync(async (ledger, db) =>
        {
            db.NzbKingKeyStates.Add(Key("idle", Origin, lastUse: Origin));
            await db.SaveChangesAsync();

            var due = await ledger.GetKeysDueForKeepaliveAsync(Origin + NzbKingTokenPolicy.KeepaliveAfter);

            Assert.Single(due);
            Assert.Equal("idle", due[0].KeyFingerprint);
        });
    }

    [Fact]
    public async Task GetKeysDueForKeepaliveAsync_LeavesAKeyThatIsNotYetIdleEnough()
    {
        await WithLedgerAsync(async (ledger, db) =>
        {
            db.NzbKingKeyStates.Add(Key("recent", Origin, lastUse: Origin));
            await db.SaveChangesAsync();

            var due = await ledger.GetKeysDueForKeepaliveAsync(Origin.AddDays(27));

            Assert.Empty(due);
        });
    }

    [Fact]
    public async Task GetKeysDueForKeepaliveAsync_SkipsAKeyAlreadyDeleted()
    {
        await WithLedgerAsync(async (ledger, db) =>
        {
            // Touching a deleted key would spend against an allowance that no longer
            // exists and log a pointless failure every cycle.
            db.NzbKingKeyStates.Add(Key("gone", Origin, lastUse: Origin, deletedAt: Origin.AddDays(1)));
            await db.SaveChangesAsync();

            var due = await ledger.GetKeysDueForKeepaliveAsync(Origin.AddDays(60));

            Assert.Empty(due);
        });
    }

    [Fact]
    public async Task GetKeysDueForKeepaliveAsync_MeasuresANeverUsedKeyFromWhenItWasRecorded()
    {
        await WithLedgerAsync(async (ledger, db) =>
        {
            db.NzbKingKeyStates.Add(Key("never-used", Origin));
            await db.SaveChangesAsync();

            Assert.Empty(await ledger.GetKeysDueForKeepaliveAsync(Origin.AddDays(27)));
            Assert.Single(await ledger.GetKeysDueForKeepaliveAsync(Origin.AddDays(28)));
        });
    }
}
