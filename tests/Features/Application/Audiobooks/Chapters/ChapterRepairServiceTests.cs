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
using Listenarr.Application.Audiobooks.Chapters;
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Chapters
{
    [Trait("Name", "ChapterRepairServiceTests")]
    [Trait("Category", "Tagging")]
    public sealed class ChapterRepairServiceTests : BaseTests
    {
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly Mock<IAudiobookTagWriter> _writer = new();
        private readonly Mock<IChapterAtomRecovery> _recovery = new();
        private readonly Mock<ITagQueueService> _queue = new();
        private readonly Mock<IFileSystem> _fileSystem = new();
        private readonly Mock<IAudnexusService> _audnexus = new();

        private static readonly TimeSpan Duration = TimeSpan.FromMinutes(100);

        private ChapterRepairService BuildService() => new(
            _audiobooks.Object,
            _writer.Object,
            _recovery.Object,
            _queue.Object,
            _fileSystem.Object,
            NullLogger<ChapterRepairService>.Instance,
            _audnexus.Object);

        private static List<EmbeddedChapter> Evenly(int count, TimeSpan each)
        {
            var chapters = new List<EmbeddedChapter>(count);
            for (var i = 0; i < count; i++)
            {
                chapters.Add(new EmbeddedChapter($"The {i + 1}th", each * i, each * (i + 1)));
            }

            return chapters;
        }

        private Audiobook GivenBook(string? asin = null)
        {
            var audiobook = new AudiobookBuilder().WithId(7).WithTitle("A Book").WithBasePath("/library/book").Build();
            audiobook.Asin = asin;
            var file = AudiobookFile.CreateUnresolved("Book.m4b");
            file.Id = 41;
            audiobook.Files = [file];
            _audiobooks.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(audiobook);
            _fileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
            return audiobook;
        }

        private void GivenFileReads(IReadOnlyList<EmbeddedChapter> played, ChapterAtomState atoms) =>
            _writer
                .Setup(w => w.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AudiobookFileTags(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    played.Count,
                    Duration,
                    false,
                    Chapters: played,
                    Atoms: atoms));

        [Fact]
        public async Task PreviewAsync_PlansFromTheChapterTrackWhenTheAtomIsBroken()
        {
            GivenBook();
            GivenFileReads(Evenly(10, TimeSpan.FromMinutes(10)), new ChapterAtomState(true, "version byte is 58", 0, true));

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterHealth.Corrupt, file.Health);
            Assert.True(file.Repairable);
            Assert.Equal(ChapterSource.Played, file.Plan!.Source);
            Assert.Equal(10, file.Plan.Chapters.Count);
            // Audnexus is not asked when the file's own track answers.
            _audnexus.Verify(a => a.GetChaptersAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task PreviewAsync_FallsBackToAudnexusThenTheDamagedAtom()
        {
            GivenBook(asin: "B00X");
            GivenFileReads([], new ChapterAtomState(true, "version byte is 58", 0, false));
            _audnexus
                .Setup(a => a.GetChaptersAsync("B00X", It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new AudnexusChapterResponse
                {
                    RuntimeLengthMs = (int)Duration.TotalMilliseconds,
                    Chapters = Enumerable.Range(0, 8).Select(i => new AudnexusChapter
                    {
                        Title = $"Chapter {i + 1}",
                        StartOffsetMs = i * 12 * 60 * 1000,
                        LengthMs = 12 * 60 * 1000
                    }).ToList()
                });

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterSource.Audnexus, file.Plan!.Source);
            Assert.Equal(8, file.Plan.Chapters.Count);
        }

        [Fact]
        public async Task PreviewAsync_UsesTheDamagedAtomAsALastResort()
        {
            GivenBook();
            GivenFileReads([], new ChapterAtomState(true, "version byte is 58", 0, false));
            _recovery
                .Setup(r => r.TryRecover(It.IsAny<string>()))
                .Returns(Evenly(5, TimeSpan.FromMinutes(10)).Select(c => c with { Start = c.Start + TimeSpan.FromMinutes(20), End = c.End + TimeSpan.FromMinutes(20) }).ToList());

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterSource.RecoveredAtom, file.Plan!.Source);
            Assert.True(file.Plan.Partial);
        }

        [Fact]
        public async Task PreviewAsync_RejectsAHealthyFile()
        {
            GivenBook();
            GivenFileReads(Evenly(10, TimeSpan.FromMinutes(10)), new ChapterAtomState(true, null, 10, true));

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.False(file.Repairable);
            Assert.Contains("not corrupt", file.Rejection);
        }

        [Fact]
        public async Task EnqueueAsync_HandsOnlyTheRepairablePlansToTheQueue()
        {
            GivenBook();
            GivenFileReads(Evenly(10, TimeSpan.FromMinutes(10)), new ChapterAtomState(true, "version byte is 58", 0, true));
            IReadOnlyDictionary<int, ChapterPlan>? handed = null;
            _queue
                .Setup(q => q.EnqueueChapterRepairAsync(7, It.IsAny<IReadOnlyDictionary<int, ChapterPlan>>(), TagTrigger.Manual, It.IsAny<CancellationToken>()))
                .Callback<int, IReadOnlyDictionary<int, ChapterPlan>, TagTrigger, CancellationToken>((_, plans, _, _) => handed = plans)
                .ReturnsAsync(new TagEnqueueResult(TagEnqueueOutcome.Queued, Guid.NewGuid()));

            var result = await BuildService().EnqueueAsync(7);

            Assert.Equal(TagEnqueueOutcome.Queued, result.Outcome);
            Assert.Equal([41], handed!.Keys);
        }

        [Fact]
        public async Task EnqueueAsync_RefusesWithTheReasonWhenNothingIsRepairable()
        {
            GivenBook();
            GivenFileReads(Evenly(10, TimeSpan.FromMinutes(10)), new ChapterAtomState(true, null, 10, true));

            var result = await BuildService().EnqueueAsync(7);

            Assert.Equal(TagEnqueueOutcome.NothingToTag, result.Outcome);
            Assert.Contains("not corrupt", result.Reason);
            _queue.VerifyNoOtherCalls();
        }
    }
}
