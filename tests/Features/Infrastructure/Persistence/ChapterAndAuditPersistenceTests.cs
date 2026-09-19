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
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The chapter and audit columns as the repositories keep them: what a book's list
/// row says about its files, and what happens to an audit verdict when the record
/// under it changes.
/// </summary>
[Trait("Name", "ChapterAndAuditPersistenceTests")]
[Trait("Category", "Infrastructure")]
public sealed class ChapterAndAuditPersistenceTests : BaseTests
{
    private const string Opening =
        "Macmillan Audio presents A War of Gifts, an Ender story, by Orson Scott Card. Read by Scott Brick.";

    private static DbContextOptions<ListenArrDbContext> Options() =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public async Task GetWorstChapterHealth_IsRedWhenAnyFlaggedFileCannotBeFixed()
    {
        var options = Options();
        await using (var seed = new ListenArrDbContext(options))
        {
            seed.Audiobooks.Add(new Audiobook { Id = 1, Title = "Two files", BasePath = "/library/two" });
            seed.AudiobookFiles.AddRange(
                File(1, "/library/two/a.m4b", ChapterHealth.Corrupt, repairable: true),
                File(1, "/library/two/b.m4b", ChapterHealth.GenericTitles, repairable: false),
                File(1, "/library/two/c.m4b", ChapterHealth.Healthy, repairable: false));
            await seed.SaveChangesAsync();
        }

        await using var db = new ListenArrDbContext(options);
        var summary = await new EfAudiobookFileRepository(db).GetWorstChapterHealthByAudiobookIdAsync();

        // The worst verdict names the problem; the stuck file decides the colour.
        Assert.Equal(ChapterHealth.Corrupt, summary[1].Health);
        Assert.False(summary[1].Repairable);
    }

    [Fact]
    public async Task GetWorstChapterHealth_IsAmberWhenEveryFlaggedFileCanBeFixed()
    {
        var options = Options();
        await using (var seed = new ListenArrDbContext(options))
        {
            seed.Audiobooks.Add(new Audiobook { Id = 1, Title = "Two files", BasePath = "/library/two" });
            seed.AudiobookFiles.AddRange(
                File(1, "/library/two/a.m4b", ChapterHealth.Healthy, repairable: false),
                File(1, "/library/two/b.m4b", ChapterHealth.Oversegmented, repairable: true));
            await seed.SaveChangesAsync();
        }

        await using var db = new ListenArrDbContext(options);
        var summary = await new EfAudiobookFileRepository(db).GetWorstChapterHealthByAudiobookIdAsync();

        // A healthy file has nothing to fix; it must not count against the book.
        Assert.Equal(ChapterHealth.Oversegmented, summary[1].Health);
        Assert.True(summary[1].Repairable);
    }

    [Fact]
    public async Task ClearChapterPlanAsync_ForgetsThePlanButNotTheVerdict()
    {
        var options = Options();
        await using (var seed = new ListenArrDbContext(options))
        {
            seed.Audiobooks.Add(new Audiobook { Id = 1, Title = "Planned", BasePath = "/library/planned" });
            var file = File(1, "/library/planned/a.m4b", ChapterHealth.Corrupt, repairable: true);
            file.ChapterPlanJson = "{}";
            file.ChapterPlanKey = "k";
            file.ChapterPlannedAt = DateTime.UtcNow;
            seed.AudiobookFiles.Add(file);
            await seed.SaveChangesAsync();
        }

        await using (var db = new ListenArrDbContext(options))
        {
            var id = await db.AudiobookFiles.Select(f => f.Id).SingleAsync();
            await new EfAudiobookFileRepository(db).ClearChapterPlanAsync([id]);
        }

        await using var verification = new ListenArrDbContext(options);
        var cleared = await verification.AudiobookFiles.AsNoTracking().SingleAsync();
        Assert.Null(cleared.ChapterPlanJson);
        Assert.Null(cleared.ChapterPlanKey);
        Assert.Null(cleared.ChapterPlannedAt);
        Assert.Equal(ChapterHealth.Corrupt, cleared.ChapterHealth);
    }

    [Fact]
    public async Task UpdateAsync_JudgesTheAuditAgainWhenTheRecordChanges()
    {
        var options = Options();
        await using (var seed = new ListenArrDbContext(options))
        {
            seed.Audiobooks.Add(new Audiobook
            {
                Id = 1,
                Title = "Ender's Game",
                Authors = ["Orson Scott Card"],
                BasePath = "/library/wog",
                AudioAuditVerdict = AudioAuditVerdict.Mismatch,
                AudioAuditReason = "The audio introduces itself as another book.",
                AudioAuditHeard = Opening,
                AudioAuditHeardTitle = "A War of Gifts",
                AudioAuditedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = new ListenArrDbContext(options))
        {
            var repository = new AudiobookRepository(db);
            var audiobook = await db.Audiobooks.SingleAsync();
            audiobook.Title = "A War of Gifts";
            await repository.UpdateAsync(audiobook);
        }

        await using var verification = new ListenArrDbContext(options);
        var fixedUp = await verification.Audiobooks.AsNoTracking().SingleAsync();
        // Nothing was listened to again: the stored transcript, judged against the corrected record.
        Assert.Equal(AudioAuditVerdict.Match, fixedUp.AudioAuditVerdict);
        Assert.Equal(Opening, fixedUp.AudioAuditHeard);
        Assert.Equal("A War of Gifts", fixedUp.AudioAuditHeardTitle);
    }

    [Fact]
    public async Task UpdateAsync_LeavesTheVerdictAloneWhenTheRecordDidNot()
    {
        var options = Options();
        await using (var seed = new ListenArrDbContext(options))
        {
            seed.Audiobooks.Add(new Audiobook
            {
                Id = 1,
                Title = "Ender's Game",
                Authors = ["Orson Scott Card"],
                BasePath = "/library/wog",
                AudioAuditVerdict = AudioAuditVerdict.Mismatch,
                AudioAuditReason = "kept",
                AudioAuditHeard = Opening
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = new ListenArrDbContext(options))
        {
            var audiobook = await db.Audiobooks.SingleAsync();
            audiobook.Publisher = "Macmillan Audio";
            await new AudiobookRepository(db).UpdateAsync(audiobook);
        }

        await using var verification = new ListenArrDbContext(options);
        var unchanged = await verification.Audiobooks.AsNoTracking().SingleAsync();
        Assert.Equal(AudioAuditVerdict.Mismatch, unchanged.AudioAuditVerdict);
        Assert.Equal("kept", unchanged.AudioAuditReason);
    }

    private static AudiobookFile File(int audiobookId, string path, ChapterHealth health, bool repairable)
    {
        var file = AudiobookFile.CreateUnresolved(path);
        file.AudiobookId = audiobookId;
        file.ChapterHealth = health;
        file.ChapterRepairable = repairable;
        return file;
    }
}
