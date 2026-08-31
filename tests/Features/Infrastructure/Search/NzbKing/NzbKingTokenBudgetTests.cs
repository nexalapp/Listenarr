using Listenarr.Application.Search.NzbKing;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Infrastructure.Search.NzbKing;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Search.NzbKing;

[Trait("Name", "NzbKingTokenBudgetTests")]
[Trait("Category", "NzbKingTokenBudget")]
public sealed class NzbKingTokenBudgetTests : BaseTests
{
    private const string ApiKey = "CM2YWCp5ZK8";

    // Real SQLite rather than the in-memory provider: the budget's protection against
    // overspending is a transaction, and the in-memory provider silently has none.
    private static async Task WithBudgetAsync(
        Func<Func<ListenArrDbContext>, INzbKingTokenBudget, Task> body,
        DateTimeOffset? now = null)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"listenarr-nzbking-{Guid.NewGuid():N}.db");
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
            await using (var setup = new ListenArrDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
            }

            ListenArrDbContext CreateContext() => new(options);

            var time = new FixedTimeProvider(now ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var budget = CreateBudget(CreateContext(), time);

            await body(CreateContext, budget);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static INzbKingTokenBudget CreateBudget(
        ListenArrDbContext db,
        TimeProvider time,
        IToastService? toasts = null) =>
        new NzbKingTokenBudget(
            new EfNzbKingLedgerRepository(db),
            Mock.Of<IAppMetricsService>(),
            toasts ?? Mock.Of<IToastService>(),
            time,
            Mock.Of<ILogger<NzbKingTokenBudget>>());

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task TryAcquireAsync_FirstUseOfAKeySpendsFromAFullAllowance()
    {
        await WithBudgetAsync(async (_, budget) =>
        {
            var lease = await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, "a book");

            Assert.True(lease.Granted);
            Assert.Equal(NzbKingTokenPolicy.MaxTokens - 1, lease.BalanceAfter);
            Assert.Equal(NzbKingKeyFingerprint.Compute(ApiKey), lease.KeyFingerprint);
        });
    }

    [Fact]
    public async Task TryAcquireAsync_StoresOnlyTheFingerprintNeverTheKey()
    {
        await WithBudgetAsync(async (createContext, budget) =>
        {
            await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, "a book");

            await using var db = createContext();
            var states = await db.NzbKingKeyStates.AsNoTracking().ToListAsync();
            var accesses = await db.NzbKingApiAccesses.AsNoTracking().ToListAsync();

            Assert.NotEmpty(states);
            Assert.All(states, state => Assert.DoesNotContain(ApiKey, state.KeyFingerprint, StringComparison.Ordinal));
            Assert.All(accesses, access => Assert.DoesNotContain(ApiKey, access.KeyFingerprint, StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task TryAcquireAsync_WithoutAKeyIsRefusedAndSpendsNothing()
    {
        await WithBudgetAsync(async (_, budget) =>
        {
            var lease = await budget.TryAcquireAsync(string.Empty, NzbKingAccessPurpose.Grab);

            Assert.False(lease.Granted);
            Assert.Null(lease.AccessId);
            Assert.NotNull(lease.Reason);
        });
    }

    [Fact]
    public async Task TryAcquireAsync_RefusesAtTheReserveAndARefusalCostsNothing()
    {
        await WithBudgetAsync(async (_, budget) =>
        {
            var spendable = NzbKingTokenPolicy.MaxTokens - NzbKingTokenPolicy.ReserveFloor;
            for (var i = 0; i < spendable; i++)
            {
                Assert.True((await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, $"book {i}")).Granted);
            }

            var refused = await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, "one too many");

            Assert.False(refused.Granted);
            Assert.Equal(NzbKingTokenPolicy.ReserveFloor, refused.BalanceAfter);
            Assert.Contains("exhausted", refused.Reason, StringComparison.OrdinalIgnoreCase);

            // Repeated denials must not drain the reserve they exist to protect.
            var again = await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, "still too many");
            Assert.Equal(NzbKingTokenPolicy.ReserveFloor, again.BalanceAfter);
        });
    }

    [Fact]
    public async Task TryAcquireAsync_LogsEveryAttemptIncludingRefusals()
    {
        await WithBudgetAsync(async (createContext, budget) =>
        {
            var spendable = NzbKingTokenPolicy.MaxTokens - NzbKingTokenPolicy.ReserveFloor;
            for (var i = 0; i < spendable; i++)
            {
                await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, $"book {i}");
            }

            await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, "one too many");

            await using var db = createContext();
            var accesses = await db.NzbKingApiAccesses.AsNoTracking().ToListAsync();

            Assert.Equal(spendable, accesses.Count(a => a.Outcome == NzbKingAccessOutcome.Spent));
            Assert.Equal(1, accesses.Count(a => a.Outcome == NzbKingAccessOutcome.DeniedByBudget));
        });
    }

    [Fact]
    public async Task ReportOutcomeAsync_TreatsA429AsDeletionAndBlocksFurtherSpending()
    {
        await WithBudgetAsync(async (_, budget) =>
        {
            var lease = await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, "a book");
            Assert.True(lease.Granted);

            await budget.ReportOutcomeAsync(lease, 429);

            var status = await budget.GetStatusAsync(ApiKey);
            Assert.NotNull(status);
            Assert.True(status!.KeyDeleted);

            var afterDeletion = await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, "another");
            Assert.False(afterDeletion.Granted);
            Assert.Contains("deleted", afterDeletion.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ReportOutcomeAsync_RecordsSuccessSoTheKeepaliveClockRestarts()
    {
        await WithBudgetAsync(async (_, budget) =>
        {
            var lease = await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Keepalive, "audiobook");

            await budget.ReportOutcomeAsync(lease, 200);

            var status = await budget.GetStatusAsync(ApiKey);
            Assert.NotNull(status);
            Assert.NotNull(status!.LastSuccessfulUseAt);
            Assert.False(status.KeyDeleted);
        });
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsNullForAKeyNeverUsed()
    {
        await WithBudgetAsync(async (_, budget) =>
        {
            Assert.Null(await budget.GetStatusAsync("a-key-that-was-never-spent"));
        });
    }

    [Fact]
    public async Task ARefusalInterruptsButAnOrdinarySpendDoesNot()
    {
        // A refusal blocked something that was asked for. A spend is already visible in
        // the grab that caused it, and a toast per spend is how toasts get ignored.
        await WithBudgetAsync(async (createContext, _) =>
        {
            var toasts = new Mock<IToastService>();
            var time = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var budget = CreateBudget(createContext(), time, toasts.Object);

            await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, "a book");

            toasts.Verify(t => t.PublishToastAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
                Times.Never);

            var spendable = NzbKingTokenPolicy.MaxTokens - NzbKingTokenPolicy.ReserveFloor;
            for (var i = 1; i < spendable; i++)
            {
                await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, $"book {i}");
            }

            await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, "one too many");

            toasts.Verify(t => t.PublishToastAsync(
                "warning", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
                Times.AtLeastOnce);
        });
    }

    [Fact]
    public async Task TheLowBalanceNoticeIsRaisedOnCrossingNotOnEverySpendBelowIt()
    {
        // Repeating it on every spend below the line turns a useful warning into wallpaper.
        await WithBudgetAsync(async (createContext, _) =>
        {
            var toasts = new Mock<IToastService>();
            var time = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var budget = CreateBudget(createContext(), time, toasts.Object);

            // Spend well past the threshold so several spends happen below it.
            for (var i = 0; i < 85; i++)
            {
                await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, $"book {i}");
            }

            toasts.Verify(t => t.PublishNotificationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()),
                Times.Once);
        });
    }

    [Fact]
    public async Task ABrokenToastServiceDoesNotBreakTheGrab()
    {
        // Reporting on a thing must not be able to take that thing down.
        await WithBudgetAsync(async (createContext, _) =>
        {
            var toasts = new Mock<IToastService>();
            toasts.Setup(t => t.PublishToastAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ThrowsAsync(new InvalidOperationException("hub is down"));
            toasts.Setup(t => t.PublishNotificationAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()))
                .ThrowsAsync(new InvalidOperationException("hub is down"));

            var time = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var budget = CreateBudget(createContext(), time, toasts.Object);

            var spendable = NzbKingTokenPolicy.MaxTokens - NzbKingTokenPolicy.ReserveFloor;
            for (var i = 0; i < spendable; i++)
            {
                await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, $"book {i}");
            }

            var refused = await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, "one too many");

            Assert.False(refused.Granted);
            Assert.Contains("exhausted", refused.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task TryAcquireAsync_ManyCallersEachReceiveTheirOwnToken()
    {
        // Each caller must come away with a distinct post-spend balance, and the ledger
        // must agree with how many were granted.
        //
        // Caveat, deliberately recorded: this does NOT prove the spend is atomic under a
        // genuine interleaving. Removing the transaction from TrySpendAsync leaves this
        // test green, because SQLite serialises these writers and the callers never
        // actually overlap. A real lost update would drift the estimate one token high,
        // which is part of why ReserveFloor exists.
        await WithBudgetAsync(async (createContext, _) =>
        {
            const int callers = 8;
            var time = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var leases = await Task.WhenAll(Enumerable.Range(0, callers).Select(async i =>
            {
                var budget = CreateBudget(createContext(), time);
                return await budget.TryAcquireAsync(ApiKey, NzbKingAccessPurpose.Grab, $"book {i}");
            }));

            var granted = leases.Where(lease => lease.Granted).ToList();
            var balances = granted.Select(lease => lease.BalanceAfter).ToList();

            // Two callers reporting the same post-spend balance would mean a token was
            // handed out twice.
            Assert.Equal(balances.Count, balances.Distinct().Count());

            await using var db = createContext();
            var state = await db.NzbKingKeyStates.AsNoTracking().SingleAsync();
            Assert.Equal(NzbKingTokenPolicy.MaxTokens - granted.Count, state.EstimatedBalance);
        });
    }
}
