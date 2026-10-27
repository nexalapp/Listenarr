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
/// A restart resumes the job it interrupted rather than starting another, against real
/// SQLite so the ordering is proven to translate. Three days of deploys every hour or
/// two left five books part-repaired and none finished.
/// </summary>
[Trait("Name", "EfConversionJobRepositoryResumeTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfConversionJobRepositoryResumeTests : BaseTests, IDisposable
{
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"conversion-resume-{Guid.NewGuid():N}.db");
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    private DbContextOptions<ListenArrDbContext> Options => new DbContextOptionsBuilder<ListenArrDbContext>()
        .UseSqlite($"Data Source={_databasePath};Pooling=False")
        .Options;

    private static ConversionJob Repair(int audiobookId, int enqueuedMinutesAgo, int? startedMinutesAgo = null) => new()
    {
        AudiobookId = audiobookId,
        Kind = ConversionJobKind.Repair,
        Trigger = ConversionTrigger.Manual,
        EnqueuedAt = Now.AddMinutes(-enqueuedMinutesAgo),
        StartedAt = startedMinutesAgo is { } ago ? Now.AddMinutes(-ago) : null
    };

    private async Task SeedAsync(params ConversionJob[] jobs)
    {
        await using var db = new ListenArrDbContext(Options);
        await db.Database.EnsureCreatedAsync();
        db.ConversionJobs.AddRange(jobs);
        await db.SaveChangesAsync();
    }

    private async Task<ConversionJob?> ClaimAsync(string owner)
    {
        await using var db = new ListenArrDbContext(Options);
        return await new EfConversionJobRepository(db)
            .ClaimNextAsync(owner, ConversionJobKind.Repair, Now, TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task ClaimNextAsync_TakesWorkAlreadyBegunBeforeAnythingQueuedEarlier()
    {
        await SeedAsync(
            Repair(1, enqueuedMinutesAgo: 300),
            Repair(2, enqueuedMinutesAgo: 290, startedMinutesAgo: 60),
            Repair(3, enqueuedMinutesAgo: 280, startedMinutesAgo: 120));

        var order = new List<int>();
        while (await ClaimAsync("worker") is { } job)
        {
            order.Add(job.AudiobookId);
        }

        // The longest-running first, then the rest in the order they were asked for.
        Assert.Equal([3, 2, 1], order);
    }

    [Fact]
    public async Task ReleaseLeaseAsync_HandsTheJobStraightBack_SoTheNextProcessResumesIt()
    {
        await SeedAsync(Repair(1, enqueuedMinutesAgo: 300), Repair(2, enqueuedMinutesAgo: 200));
        var interrupted = await ClaimAsync("old-process");
        Assert.Equal(1, interrupted!.AudiobookId);

        bool released;
        await using (var db = new ListenArrDbContext(Options))
        {
            released = await new EfConversionJobRepository(db).ReleaseLeaseAsync(interrupted.Id, "old-process", Now);
        }

        Assert.True(released);
        Assert.Equal(1, (await ClaimAsync("new-process"))!.AudiobookId);
    }

    [Fact]
    public async Task ReleaseLeaseAsync_LeavesAJobAnotherWorkerOwns()
    {
        await SeedAsync(Repair(1, enqueuedMinutesAgo: 300));
        var job = await ClaimAsync("owner");

        await using var db = new ListenArrDbContext(Options);
        Assert.False(await new EfConversionJobRepository(db).ReleaseLeaseAsync(job!.Id, "someone-else", Now));
        Assert.Equal(ConversionJobStatus.Running, (await db.ConversionJobs.SingleAsync()).Status);
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
