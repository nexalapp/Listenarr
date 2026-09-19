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
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>The not-found flag as the repository keeps it: set once, cleared, counted, removed on request.</summary>
[Trait("Name", "NotFoundFilesPersistenceTests")]
[Trait("Category", "Infrastructure")]
public sealed class NotFoundFilesPersistenceTests : BaseTests
{
    private static readonly DateTime First = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<ListenArrDbContext> Options() =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task<(int A, int B)> SeedAsync(DbContextOptions<ListenArrDbContext> options)
    {
        await using var seed = new ListenArrDbContext(options);
        seed.Audiobooks.Add(new Audiobook { Id = 1, Title = "Book", BasePath = "/library/book" });
        var a = AudiobookFile.CreateUnresolved("/library/book/a.m4b");
        a.AudiobookId = 1;
        var b = AudiobookFile.CreateUnresolved("/library/book/b.m4b");
        b.AudiobookId = 1;
        seed.AudiobookFiles.AddRange(a, b);
        await seed.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task MarkNotFoundAsync_SetsTheFlagOnceAndReportsWhatItSet()
    {
        var options = Options();
        var (a, b) = await SeedAsync(options);

        await using var db = new ListenArrDbContext(options);
        var repository = new EfAudiobookFileRepository(db);
        var first = await repository.MarkNotFoundAsync([a], First);
        var second = await repository.MarkNotFoundAsync([a, b], Later);

        // The first absence is the one worth remembering; a re-mark changes nothing.
        Assert.Equal([a], first);
        Assert.Equal([b], second);
        var rows = await db.AudiobookFiles.AsNoTracking().OrderBy(f => f.Id).ToListAsync();
        Assert.Equal(First, rows[0].NotFoundSinceUtc);
        Assert.Equal(Later, rows[1].NotFoundSinceUtc);
        Assert.Equal(2, (await repository.GetNotFoundCountsByAudiobookIdAsync())[1]);
    }

    [Fact]
    public async Task ClearNotFoundAsync_TakesTheFlagOff()
    {
        var options = Options();
        var (a, _) = await SeedAsync(options);

        await using var db = new ListenArrDbContext(options);
        var repository = new EfAudiobookFileRepository(db);
        await repository.MarkNotFoundAsync([a], First);
        await repository.ClearNotFoundAsync([a]);

        Assert.Null((await db.AudiobookFiles.AsNoTracking().SingleAsync(f => f.Id == a)).NotFoundSinceUtc);
        Assert.Empty(await repository.GetNotFoundCountsByAudiobookIdAsync());
    }

    [Fact]
    public async Task DeleteNotFoundAsync_RemovesOnlyFlaggedRows()
    {
        var options = Options();
        var (a, b) = await SeedAsync(options);

        await using var db = new ListenArrDbContext(options);
        var repository = new EfAudiobookFileRepository(db);
        await repository.MarkNotFoundAsync([a], First);
        var removed = await repository.DeleteNotFoundAsync(1);

        var gone = Assert.Single(removed);
        Assert.Equal(a, gone.Id);
        Assert.Equal("/library/book/a.m4b", gone.Path);
        var remaining = Assert.Single(await db.AudiobookFiles.AsNoTracking().ToListAsync());
        Assert.Equal(b, remaining.Id);
        Assert.Empty(await repository.DeleteNotFoundAsync(1));
    }
}
