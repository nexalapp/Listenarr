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

/// <summary>
/// The order the tag worker takes jobs in, against real SQLite so the ordering
/// expression is proven to translate.
/// </summary>
[Trait("Name", "EfTagJobRepositoryClaimOrderTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfTagJobRepositoryClaimOrderTests : BaseTests, IDisposable
{
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"tag-claim-order-{Guid.NewGuid():N}.db");
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private DbContextOptions<ListenArrDbContext> Options => new DbContextOptionsBuilder<ListenArrDbContext>()
        .UseSqlite($"Data Source={_databasePath};Pooling=False")
        .Options;

    private static TagJob Job(int audiobookId, TagJobKind kind, TagTrigger trigger, int minutesAgo) => new()
    {
        AudiobookId = audiobookId,
        Kind = kind,
        Trigger = trigger,
        FileCount = 1,
        SelectedFileIdsJson = "[1]",
        EnqueuedAt = Now.AddMinutes(-minutesAgo),
        UpdatedAt = Now.AddMinutes(-minutesAgo)
    };

    private async Task SeedAsync(params TagJob[] jobs)
    {
        await using var db = new ListenArrDbContext(Options);
        await db.Database.EnsureCreatedAsync();
        db.TagJobs.AddRange(jobs);
        await db.SaveChangesAsync();
    }

    private async Task<List<int>> ClaimAllAsync()
    {
        var claimed = new List<int>();
        await using var db = new ListenArrDbContext(Options);
        var repository = new EfTagJobRepository(db);
        while (await repository.ClaimNextAsync("worker", Now, TimeSpan.FromMinutes(10)) is { } job)
        {
            claimed.Add(job.AudiobookId);
        }

        return claimed;
    }

    [Fact]
    public async Task ClaimNextAsync_TakesWritesThenManualListeningThenBackgroundPlanning_OldestFirstWithinEach()
    {
        await SeedAsync(
            Job(1, TagJobKind.Plan, TagTrigger.Automatic, minutesAgo: 300),
            Job(2, TagJobKind.Plan, TagTrigger.Automatic, minutesAgo: 290),
            Job(3, TagJobKind.Audit, TagTrigger.Automatic, minutesAgo: 280),
            Job(4, TagJobKind.Plan, TagTrigger.Manual, minutesAgo: 60),
            Job(5, TagJobKind.Tags, TagTrigger.Manual, minutesAgo: 10),
            Job(6, TagJobKind.Chapters, TagTrigger.Manual, minutesAgo: 5),
            Job(7, TagJobKind.Tags, TagTrigger.Automatic, minutesAgo: 20));

        var order = await ClaimAllAsync();

        // Writes (7 is the oldest write), then the manual plan, then the automatic listening in age order.
        Assert.Equal([7, 5, 6, 4, 1, 2, 3], order);
    }

    [Fact]
    public async Task ClaimNextAsync_LeavesAJobWhoseRetryIsNotDueYet()
    {
        var notYet = Job(1, TagJobKind.Tags, TagTrigger.Manual, minutesAgo: 30);
        notYet.Status = TagJobStatus.RetryScheduled;
        notYet.NextAttemptAt = Now.AddMinutes(5);
        await SeedAsync(notYet, Job(2, TagJobKind.Plan, TagTrigger.Automatic, minutesAgo: 20));

        Assert.Equal([2], await ClaimAllAsync());
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
        }
    }
}
