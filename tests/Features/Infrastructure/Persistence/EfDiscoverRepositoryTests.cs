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
using Listenarr.Domain.Audiobooks.Discover;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// How the discovery cache holds what it has been told.
///
/// <para>
/// The rule worth pinning is that the two sources are kept differently. A chart page
/// is a set of positions, so re-reading it must drop what has fallen off; a board page
/// is a window onto a list that grows from the top, and re-reading it must not drop
/// the topics that have been pushed onto the next page.
/// </para>
/// </summary>
[Trait("Name", "EfDiscoverRepositoryTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfDiscoverRepositoryTests : BaseTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<ListenArrDbContext> Options() =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public async Task UpsertItems_AddsWhatIsNewAndKeepsWhenAnItemWasFirstOffered()
    {
        var options = Options();
        await using var db = new ListenArrDbContext(options);
        var repository = new EfDiscoverRepository(db);

        await repository.UpsertItemsAsync(
            DiscoverSources.AudibleCharts, "1", [Item("B1", 1, 1, firstSeen: Now)], replacePage: 1);
        await repository.UpsertItemsAsync(
            DiscoverSources.AudibleCharts,
            "1",
            [Item("B1", 4, 1, firstSeen: Now.AddDays(1)), Item("B2", 5, 1)],
            replacePage: 1);

        var items = await repository.GetItemsAsync(DiscoverSources.AudibleCharts, ["1"]);
        var first = items.Single(item => item.ExternalId == "B1");
        Assert.Equal(4, first.Rank);
        Assert.Equal(Now, first.FirstSeenAt);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task UpsertItems_ForAChart_DropsABookThatHasLeftThePage()
    {
        var options = Options();
        await using var db = new ListenArrDbContext(options);
        var repository = new EfDiscoverRepository(db);

        await repository.UpsertItemsAsync(
            DiscoverSources.AudibleCharts, "1", [Item("B1", 1, 1), Item("B2", 2, 1)], replacePage: 1);
        await repository.UpsertItemsAsync(
            DiscoverSources.AudibleCharts, "1", [Item("B2", 1, 1)], replacePage: 1);

        var items = await repository.GetItemsAsync(DiscoverSources.AudibleCharts, ["1"]);
        Assert.Equal("B2", Assert.Single(items).ExternalId);
    }

    [Fact]
    public async Task UpsertItems_ForAChart_LeavesTheOtherPagesAlone()
    {
        var options = Options();
        await using var db = new ListenArrDbContext(options);
        var repository = new EfDiscoverRepository(db);

        await repository.UpsertItemsAsync(
            DiscoverSources.AudibleCharts, "1", [Item("B1", 1, 1)], replacePage: 1);
        await repository.UpsertItemsAsync(
            DiscoverSources.AudibleCharts, "1", [Item("B21", 21, 2)], replacePage: 2);
        await repository.UpsertItemsAsync(
            DiscoverSources.AudibleCharts, "1", [Item("B1", 1, 1)], replacePage: 1);

        var items = await repository.GetItemsAsync(DiscoverSources.AudibleCharts, ["1"]);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task UpsertItems_ForABoard_KeepsTopicsPushedOffThePage()
    {
        var options = Options();
        await using var db = new ListenArrDbContext(options);
        var repository = new EfDiscoverRepository(db);

        await repository.UpsertItemsAsync(
            DiscoverSources.AbookLink, "7", [Item("100", 1, 1), Item("99", 2, 1)]);
        // Two new posts arrive; the board's first page no longer mentions 99.
        await repository.UpsertItemsAsync(
            DiscoverSources.AbookLink, "7", [Item("102", 1, 1), Item("101", 2, 1)]);

        var items = await repository.GetItemsAsync(DiscoverSources.AbookLink, ["7"]);
        Assert.Equal(4, items.Count);
    }

    [Fact]
    public async Task UpsertItems_DoesNotOverwriteARecordThatHasBeenLookedUp()
    {
        var options = Options();
        await using var db = new ListenArrDbContext(options);
        var repository = new EfDiscoverRepository(db);

        await repository.UpsertItemsAsync(
            DiscoverSources.AbookLink, "7", [Item("100", 1, 1, enriched: false, title: "Valiant")]);
        var stored = (await repository.GetItemsAsync(DiscoverSources.AbookLink, ["7"])).Single();
        await repository.ResolveItemAsync(
            stored.Id,
            new DiscoverBook { Asin = "B9", Title = "Valiant", ImageUrl = "https://example.invalid/cover.jpg" },
            Now);

        // The board page is read again and offers only what its title says.
        await repository.UpsertItemsAsync(
            DiscoverSources.AbookLink, "7", [Item("100", 1, 1, enriched: false, title: "Valiant")]);

        var item = (await repository.GetItemsAsync(DiscoverSources.AbookLink, ["7"])).Single();
        Assert.True(item.Enriched);
        Assert.Equal("https://example.invalid/cover.jpg", item.Book.ImageUrl);
    }

    [Fact]
    public async Task GetUnresolved_OffersTheNewestPostsFirst()
    {
        var options = Options();
        await using var db = new ListenArrDbContext(options);
        var repository = new EfDiscoverRepository(db);

        await repository.UpsertItemsAsync(
            DiscoverSources.AbookLink,
            "7",
            [
                Item("100", 1, 1, enriched: false, postedAt: Now.AddDays(-3)),
                Item("101", 2, 1, enriched: false, postedAt: Now),
                Item("102", 3, 1, enriched: true, postedAt: Now.AddDays(-1))
            ]);

        var unresolved = await repository.GetUnresolvedAsync(DiscoverSources.AbookLink, 5);

        Assert.Equal(["101", "100"], unresolved.Select(item => item.ExternalId));
    }

    [Fact]
    public async Task TrimSection_KeepsTheNewestAndDropsTheRest()
    {
        var options = Options();
        await using var db = new ListenArrDbContext(options);
        var repository = new EfDiscoverRepository(db);

        await repository.UpsertItemsAsync(
            DiscoverSources.AbookLink,
            "7",
            [
                Item("100", 1, 1, postedAt: Now.AddDays(-3)),
                Item("101", 2, 1, postedAt: Now.AddDays(-1)),
                Item("102", 3, 1, postedAt: Now)
            ]);

        var removed = await repository.TrimSectionAsync(DiscoverSources.AbookLink, "7", 2);

        Assert.Equal(1, removed);
        var items = await repository.GetItemsAsync(DiscoverSources.AbookLink, ["7"]);
        Assert.Equal(["102", "101"], items.OrderByDescending(item => item.PostedAt).Select(item => item.ExternalId));
    }

    [Fact]
    public async Task PruneSections_ForgetsWhatIsNoLongerFollowed()
    {
        var options = Options();
        await using var db = new ListenArrDbContext(options);
        var repository = new EfDiscoverRepository(db);

        await repository.UpsertItemsAsync(DiscoverSources.AbookLink, "7", [Item("100", 1, 1)]);
        await repository.UpsertItemsAsync(DiscoverSources.AbookLink, "9", [Item("200", 1, 1)]);
        await repository.SaveSectionAsync(
            DiscoverSources.AbookLink, "9", "Crime", 1, null, false, null, Now);

        var removed = await repository.PruneSectionsAsync(DiscoverSources.AbookLink, ["7"]);

        Assert.Equal(2, removed);
        Assert.Empty(await repository.GetItemsAsync(DiscoverSources.AbookLink, ["9"]));
        Assert.Single(await repository.GetItemsAsync(DiscoverSources.AbookLink, ["7"]));
    }

    [Fact]
    public async Task SaveSection_RecordsProgressAndThenClearsTheErrorWhenOneSucceeds()
    {
        var options = Options();
        await using var db = new ListenArrDbContext(options);
        var repository = new EfDiscoverRepository(db);

        await repository.SaveSectionAsync(
            DiscoverSources.AudibleCharts, "1", "Science Fiction & Fantasy", 0, null, false, "It broke", Now);
        await repository.SaveSectionAsync(
            DiscoverSources.AudibleCharts, "1", "Science Fiction & Fantasy", 2, 100, false, null, Now.AddHours(1));

        var section = Assert.Single(await repository.GetSectionsAsync(DiscoverSources.AudibleCharts));
        Assert.Equal(2, section.PagesFetched);
        Assert.Equal(100, section.TotalItems);
        Assert.Null(section.LastError);
    }

    private static DiscoverItem Item(
        string externalId,
        int rank,
        int page,
        bool enriched = true,
        string title = "A book",
        DateTime? postedAt = null,
        DateTime? firstSeen = null) =>
        new()
        {
            Source = string.Empty,
            SectionId = string.Empty,
            ExternalId = externalId,
            Rank = rank,
            Page = page,
            Enriched = enriched,
            PostedAt = postedAt,
            FirstSeenAt = firstSeen ?? Now,
            UpdatedAt = firstSeen ?? Now,
            Book = new DiscoverBook { Title = title }
        };
}
