using Listenarr.Application.Search.NzbKing;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Infrastructure.Search.NzbKing;
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

    [Fact]
    public async Task EnsureKeyAsync_RecordsAConfiguredKeyNothingHasSpentAgainst()
    {
        // The hole this closes. A key was only recorded by a spend, and the selection above
        // can only see recorded keys, so a configured key that no grab ever charged was
        // never kept alive and NZBKing deleted it after a month. With a pool that is the
        // normal case, not the unlucky one: every key but the one the spend order picks
        // first starts out unrecorded.
        await WithLedgerAsync(async (ledger, _) =>
        {
            Assert.True(await ledger.EnsureKeyAsync("fresh", Origin));

            Assert.Empty(await ledger.GetKeysDueForKeepaliveAsync(Origin.AddDays(27)));
            Assert.Single(await ledger.GetKeysDueForKeepaliveAsync(Origin.AddDays(28)));
        });
    }

    [Fact]
    public async Task EnsureKeyAsync_RecordsAFullAllowance()
    {
        await WithLedgerAsync(async (ledger, _) =>
        {
            await ledger.EnsureKeyAsync("fresh", Origin);

            var state = await ledger.GetByFingerprintAsync("fresh");

            Assert.NotNull(state);
            Assert.Equal(NzbKingTokenPolicy.MaxTokens, state!.EstimatedBalance);
            Assert.Null(state.LastSuccessfulUseAt);
            Assert.Null(state.KeyDeletedAt);
        });
    }

    [Fact]
    public async Task EnsureKeyAsync_RunsEveryCycleWithoutRestartingTheIdleClock()
    {
        // It runs daily against every configured key, so touching an existing row would
        // push its idle clock forward every cycle and no key would ever become due. The
        // key would then be deleted by NZBKing while this reported it as freshly seen.
        await WithLedgerAsync(async (ledger, _) =>
        {
            await ledger.EnsureKeyAsync("fresh", Origin);

            Assert.False(await ledger.EnsureKeyAsync("fresh", Origin.AddDays(20)));
            Assert.False(await ledger.EnsureKeyAsync("fresh", Origin.AddDays(27)));

            var due = await ledger.GetKeysDueForKeepaliveAsync(Origin.AddDays(28));

            Assert.Single(due);
        });
    }

    [Fact]
    public async Task EnsureKeyAsync_DoesNotReviveAKeyNzbKingHasDeleted()
    {
        // The key stays configured after a 429, so this sees it on every cycle. Clearing
        // the deletion would put it back in the spend order and spend against an allowance
        // that no longer exists.
        await WithLedgerAsync(async (ledger, db) =>
        {
            db.NzbKingKeyStates.Add(Key("gone", Origin, lastUse: Origin, deletedAt: Origin.AddDays(1)));
            await db.SaveChangesAsync();

            Assert.False(await ledger.EnsureKeyAsync("gone", Origin.AddDays(40)));

            var state = await ledger.GetByFingerprintAsync("gone");

            Assert.NotNull(state!.KeyDeletedAt);
            Assert.Empty(await ledger.GetKeysDueForKeepaliveAsync(Origin.AddDays(60)));
        });
    }

    [Fact]
    public async Task EnsureKeyAsync_RecordsEveryKeyInAPoolSoEveryOneIsKeptAlive()
    {
        await WithLedgerAsync(async (ledger, _) =>
        {
            foreach (var fingerprint in new[] { "one", "two", "three" })
            {
                Assert.True(await ledger.EnsureKeyAsync(fingerprint, Origin));
            }

            var due = await ledger.GetKeysDueForKeepaliveAsync(Origin.AddDays(28));

            Assert.Equal(
                ["one", "three", "two"],
                due.Select(state => state.KeyFingerprint).Order(StringComparer.Ordinal));
        });
    }

    [Fact]
    public async Task ConfiguredKeys_AreCollectedFromEveryIndexerAndEveryKey()
    {
        // What the keepalive builds its lookup from, and what the spend order ranks. Reading
        // one key per indexer - which is what the single-key helper did - would leave the
        // rest of the pool unspent and unprotected.
        var indexers = new Mock<IIndexerRepository>();
        indexers.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer
                {
                    Name = "abook.link",
                    AdditionalSettings = """{"nzbking_api_keys":["one","two"]}"""
                },
                new Indexer
                {
                    Name = "another",
                    AdditionalSettings = """{"nzbking_api_key":"three"}"""
                },
                new Indexer { Name = "no keys", AdditionalSettings = null }
            ]);

        var keys = await NzbKingConfiguredKeys.CollectAsync(indexers.Object);

        Assert.Equal(["one", "two", "three"], keys);
    }

    [Fact]
    public async Task ConfiguredKeys_CollapseAKeyTwoIndexersShare()
    {
        var indexers = new Mock<IIndexerRepository>();
        indexers.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer { Name = "a", AdditionalSettings = """{"nzbking_api_keys":["shared"]}""" },
                new Indexer { Name = "b", AdditionalSettings = """{"nzbking_api_key":"shared"}""" }
            ]);

        var keys = await NzbKingConfiguredKeys.CollectAsync(indexers.Object);

        Assert.Equal(["shared"], keys);
    }
}
