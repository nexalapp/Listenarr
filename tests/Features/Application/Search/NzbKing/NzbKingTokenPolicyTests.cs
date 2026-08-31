using Listenarr.Application.Search.NzbKing;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.NzbKing;

[Trait("Name", "NzbKingTokenPolicyTests")]
[Trait("Category", "NzbKingTokenBudget")]
public sealed class NzbKingTokenPolicyTests : BaseTests
{
    private static readonly DateTime Origin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Accrue_GrantsOneTokenPerWholeHour()
    {
        var result = NzbKingTokenPolicy.Accrue(50, Origin, Origin.AddHours(3));

        Assert.Equal(53, result.Balance);
        Assert.Equal(Origin.AddHours(3), result.RefillAnchor);
    }

    [Fact]
    public void Accrue_IgnoresPartialHours()
    {
        var result = NzbKingTokenPolicy.Accrue(50, Origin, Origin.AddMinutes(59));

        Assert.Equal(50, result.Balance);
        Assert.Equal(Origin, result.RefillAnchor);
    }

    [Fact]
    public void Accrue_CarriesPartialHoursForwardAcrossCalls()
    {
        // Ninety minutes earns one token; the leftover thirty must survive, so that
        // thirty minutes later a second token lands. Anchoring on "now" instead would
        // discard the remainder and permanently under-count the refill.
        var first = NzbKingTokenPolicy.Accrue(50, Origin, Origin.AddMinutes(90));
        Assert.Equal(51, first.Balance);

        var second = NzbKingTokenPolicy.Accrue(first.Balance, first.RefillAnchor, Origin.AddMinutes(120));

        Assert.Equal(52, second.Balance);
    }

    [Fact]
    public void Accrue_StopsAtTheCap()
    {
        var result = NzbKingTokenPolicy.Accrue(98, Origin, Origin.AddHours(50));

        Assert.Equal(NzbKingTokenPolicy.MaxTokens, result.Balance);
    }

    [Fact]
    public void Accrue_DoesNotBankRefillsEarnedWhileAtTheCap()
    {
        // NZBKing only returns a token when you are below 100, so an idle key accumulates
        // nothing. If the anchor stalled while capped, a later spend would appear to be
        // refunded instantly from a backlog that was never actually earned.
        var idle = NzbKingTokenPolicy.Accrue(NzbKingTokenPolicy.MaxTokens, Origin, Origin.AddHours(50));
        Assert.Equal(NzbKingTokenPolicy.MaxTokens, idle.Balance);

        var afterSpending = idle.Balance - 10;
        var next = NzbKingTokenPolicy.Accrue(afterSpending, idle.RefillAnchor, Origin.AddHours(50));

        Assert.Equal(afterSpending, next.Balance);
    }

    [Fact]
    public void Accrue_DoesNotGrantOrRewindWhenTheClockGoesBackwards()
    {
        var result = NzbKingTokenPolicy.Accrue(50, Origin, Origin.AddHours(-5));

        Assert.Equal(50, result.Balance);
        Assert.Equal(Origin, result.RefillAnchor);
    }

    [Fact]
    public void CanSpend_RefusesOnceSpendingWouldBreachTheReserve()
    {
        // Spending is allowed only while a token would still be left above the reserve.
        Assert.True(NzbKingTokenPolicy.CanSpend(NzbKingTokenPolicy.ReserveFloor + 1));
        Assert.False(NzbKingTokenPolicy.CanSpend(NzbKingTokenPolicy.ReserveFloor));
        Assert.False(NzbKingTokenPolicy.CanSpend(0));
    }

    [Fact]
    public void CanSpend_RefusesWhenDriftHasPutTheBalanceUnderTheReserve()
    {
        Assert.False(NzbKingTokenPolicy.CanSpend(NzbKingTokenPolicy.ReserveFloor - 1));
    }

    [Fact]
    public void IsDueForKeepalive_UsesLastSuccessfulUseWhenPresent()
    {
        var lastUse = Origin;

        Assert.True(NzbKingTokenPolicy.IsDueForKeepalive(lastUse, Origin.AddYears(-1), lastUse.AddDays(28)));
        Assert.False(NzbKingTokenPolicy.IsDueForKeepalive(lastUse, Origin.AddYears(-1), lastUse.AddDays(27)));
    }

    [Fact]
    public void IsDueForKeepalive_FallsBackToCreationForAKeyNeverUsed()
    {
        Assert.True(NzbKingTokenPolicy.IsDueForKeepalive(null, Origin, Origin.AddDays(28)));
        Assert.False(NzbKingTokenPolicy.IsDueForKeepalive(null, Origin, Origin.AddDays(27)));
    }
}
