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
using Listenarr.Application.Common.Contracts;
using Listenarr.Domain.Configuration;
using Listenarr.Domain.Search;
using Listenarr.Infrastructure.Persistence;
using Listenarr.Infrastructure.Search.AbookLink;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Search.AbookLink;

/// <summary>
/// The budget on abook.link's thank button.
///
/// <para>
/// Thanking is the only thing Listenarr does on that forum anyone else can see, and it
/// cannot be undone. Several unrelated paths reach it - auto-grab, the automatic search
/// for monitored books, a manual add, a retry - so a limit on any one of them is not a
/// limit. These pin the two rules: a trailing-hour rate, and a breaker that shuts the
/// lane when the forum itself has started refusing.
/// </para>
/// </summary>
[Trait("Name", "AbookThankBudgetTests")]
[Trait("Category", "Infrastructure")]
public sealed class AbookThankBudgetTests : BaseTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public DateTime Utc { get; set; } = utc;

        public override DateTimeOffset GetUtcNow() => new(Utc, TimeSpan.Zero);
    }

    private static (AbookThankBudget Budget, ListenArrDbContext Db, FixedClock Clock) Build(
        int perHour = 3)
    {
        var db = new ListenArrDbContext(
            new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        var configuration = new Mock<IConfigurationService>();
        configuration
            .Setup(c => c.GetApplicationSettingsAsync())
            .ReturnsAsync(new ApplicationSettings { AbookThanksPerHour = perHour });
        var clock = new FixedClock(Now);
        return (new AbookThankBudget(db, configuration.Object, clock), db, clock);
    }

    private static async Task ThankedAsync(
        ListenArrDbContext db, DateTime at, bool succeeded, int topicId = 1)
    {
        db.AbookThanks.Add(new AbookThank
        {
            TopicId = topicId,
            ThankedAt = at,
            Succeeded = succeeded
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AThankIsAllowedWhileTheHourHasRoom()
    {
        var (budget, db, _) = Build(perHour: 3);
        await ThankedAsync(db, Now.AddMinutes(-10), true);

        var decision = await budget.CheckAsync();

        Assert.True(decision.Allowed);
        Assert.Equal(1, decision.ThanksThisHour);
    }

    [Fact]
    public async Task TheHourlyLimitRefusesAndSaysWhenThereIsRoomAgain()
    {
        var (budget, db, _) = Build(perHour: 3);
        await ThankedAsync(db, Now.AddMinutes(-50), true, 1);
        await ThankedAsync(db, Now.AddMinutes(-20), true, 2);
        await ThankedAsync(db, Now.AddMinutes(-5), true, 3);

        var decision = await budget.CheckAsync();

        Assert.False(decision.Allowed);
        Assert.Equal(3, decision.ThanksThisHour);
        // Room returns when the oldest of the three ages out, not an hour from now.
        Assert.Equal(Now.AddMinutes(-50).AddHours(1), decision.NextAllowedAt);
    }

    [Fact]
    public async Task AThankAnHourOldNoLongerCountsAgainstTheBudget()
    {
        var (budget, db, _) = Build(perHour: 1);
        await ThankedAsync(db, Now.AddMinutes(-61), true);

        var decision = await budget.CheckAsync();

        Assert.True(decision.Allowed);
        Assert.Equal(0, decision.ThanksThisHour);
    }

    [Fact]
    public async Task ThreeRefusalsInARowCloseTheLane()
    {
        // The rate is nowhere near spent; it is the forum saying no that stops us.
        var (budget, db, _) = Build(perHour: 100);
        await ThankedAsync(db, Now.AddMinutes(-3), false, 1);
        await ThankedAsync(db, Now.AddMinutes(-2), false, 2);
        await ThankedAsync(db, Now.AddMinutes(-1), false, 3);

        var decision = await budget.CheckAsync();

        Assert.False(decision.Allowed);
        Assert.Contains("refused", decision.Reason);
    }

    [Fact]
    public async Task OneSuccessAmongThemLeavesTheLaneOpen()
    {
        var (budget, db, _) = Build(perHour: 100);
        await ThankedAsync(db, Now.AddMinutes(-3), false, 1);
        await ThankedAsync(db, Now.AddMinutes(-2), true, 2);
        await ThankedAsync(db, Now.AddMinutes(-1), false, 3);

        Assert.True((await budget.CheckAsync()).Allowed);
    }

    [Fact]
    public async Task TheLaneReopensOnceTheCoolOffHasPassed()
    {
        var (budget, db, clock) = Build(perHour: 100);
        await ThankedAsync(db, Now.AddMinutes(-3), false, 1);
        await ThankedAsync(db, Now.AddMinutes(-2), false, 2);
        await ThankedAsync(db, Now.AddMinutes(-1), false, 3);

        clock.Utc = Now + AbookThankBudget.LaneReopensAfter + TimeSpan.FromMinutes(1);

        Assert.True((await budget.CheckAsync()).Allowed);
    }

    [Fact]
    public async Task RecordingAThankIsWhatLaterChecksRead()
    {
        var (budget, _, _) = Build(perHour: 1);

        Assert.True((await budget.CheckAsync()).Allowed);
        await budget.RecordAsync(4242, true, null);

        var after = await budget.CheckAsync();
        Assert.False(after.Allowed);
        Assert.Equal(1, after.ThanksThisHour);
    }

    [Fact]
    public async Task ThePanelGetsTheHourAndTheDay()
    {
        var (budget, db, _) = Build();
        await ThankedAsync(db, Now.AddMinutes(-5), true, 1);
        await ThankedAsync(db, Now.AddHours(-5), true, 2);
        await ThankedAsync(db, Now.AddDays(-2), true, 3);

        var (hour, day) = await budget.CountsAsync();

        Assert.Equal(1, hour);
        Assert.Equal(2, day);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANonsenseLimitFallsBackToTheDefault(int configured) =>
        Assert.Equal(AbookThankBudget.DefaultPerHour, AbookThankBudget.PerHourFrom(configured));
}
