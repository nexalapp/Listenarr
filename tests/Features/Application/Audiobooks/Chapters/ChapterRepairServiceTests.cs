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
using Listenarr.Application.Audiobooks.Transcription;
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
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<ITranscriber> _transcriber = new();

        private static readonly TimeSpan Duration = TimeSpan.FromMinutes(100);

        public ChapterRepairServiceTests()
        {
            GivenTranscription(enabled: false);
        }

        private ChapterRepairService BuildService() => new(
            _audiobooks.Object,
            _writer.Object,
            _recovery.Object,
            _queue.Object,
            _fileSystem.Object,
            _configuration.Object,
            NullLogger<ChapterRepairService>.Instance,
            _audnexus.Object,
            _transcriber.Object,
            new TranscriptCache());

        private void GivenTranscription(bool enabled)
        {
            var settings = new ApplicationSettingsBuilder().Build();
            settings.TranscriptionEnabled = enabled;
            _configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);
            _transcriber.Setup(t => t.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(enabled);
        }

        /// <summary>What the narrator says after each mark, by mark index; silence elsewhere.</summary>
        private void GivenHeard(IReadOnlyList<EmbeddedChapter> marks, IReadOnlyDictionary<int, string> announcements)
        {
            _fileSystem.Setup(fs => fs.GetFileLength(It.IsAny<string>())).Returns(1024);
            _fileSystem.Setup(fs => fs.GetLastWriteTimeUtc(It.IsAny<string>())).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, TimeSpan start, TimeSpan _, CancellationToken _) =>
                {
                    var index = marks.ToList().FindIndex(m => m.Start == start);
                    return announcements.TryGetValue(index, out var said) ? new Transcript(said) : new Transcript("the rain kept falling");
                });
        }

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

        // ---- listening ----------------------------------------------------------------

        private static List<EmbeddedChapter> Tracks(int count, TimeSpan each) =>
            Enumerable.Range(0, count).Select(i => new EmbeddedChapter($"Track {i + 1:D2}", each * i, each * (i + 1))).ToList();

        [Fact]
        public async Task PreviewAsync_TellsTheOperatorToTurnTranscriptionOnForCdTracks()
        {
            GivenBook();
            GivenFileReads(Tracks(30, TimeSpan.FromMinutes(3)), new ChapterAtomState(true, null, 30, true));

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterHealth.Oversegmented, file.Health);
            Assert.False(file.Repairable);
            Assert.Contains("Turn on transcription", file.Rejection);
            _transcriber.Verify(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PreviewAsync_MergesCdTracksAtTheAnnouncementsItHears()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            var marks = Tracks(30, TimeSpan.FromMinutes(3));
            GivenFileReads(marks, new ChapterAtomState(true, null, 30, true));
            GivenHeard(marks, new Dictionary<int, string> { [0] = "Chapter one.", [10] = "Chapter two. The war.", [20] = "Chapter three." });

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.True(file.Repairable);
            Assert.Equal(ChapterSource.Announcements, file.Plan!.Source);
            Assert.Equal(["Chapter 1", "Chapter 2: The war", "Chapter 3"], file.Plan.Chapters.Select(c => c.Title));
            Assert.Equal("Chapter two. The war.", file.Plan.Heard![1]);
            // Every mark was listened to, ten seconds each.
            _transcriber.Verify(
                t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), ChapterRepairService.ListenWindow, It.IsAny<CancellationToken>()),
                Times.Exactly(30));
        }

        [Fact]
        public async Task PreviewAsync_ListensOnlyOnceAcrossPreviewAndEnqueue()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            var marks = Tracks(30, TimeSpan.FromMinutes(3));
            GivenFileReads(marks, new ChapterAtomState(true, null, 30, true));
            GivenHeard(marks, new Dictionary<int, string> { [0] = "Chapter one.", [15] = "Chapter two." });
            _queue
                .Setup(q => q.EnqueueChapterRepairAsync(7, It.IsAny<IReadOnlyDictionary<int, ChapterPlan>>(), TagTrigger.Manual, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TagEnqueueResult(TagEnqueueOutcome.Queued, Guid.NewGuid()));

            var service = BuildService();
            await service.PreviewAsync(7);
            var result = await service.EnqueueAsync(7);

            Assert.Equal(TagEnqueueOutcome.Queued, result.Outcome);
            _transcriber.Verify(
                t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.Exactly(30));
        }

        [Fact]
        public async Task PreviewAsync_FlagsACdRipWhoseNarratorNeverAnnounces()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            var marks = Tracks(30, TimeSpan.FromMinutes(3));
            GivenFileReads(marks, new ChapterAtomState(true, null, 30, true));
            GivenHeard(marks, new Dictionary<int, string>());

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.False(file.Repairable);
            Assert.Contains("No chapter announcements", file.Rejection);
        }

        [Fact]
        public async Task PreviewAsync_RetitlesPlaceholderChaptersFromWhatItHears()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            var marks = Enumerable.Range(0, 5)
                .Select(i => new EmbeddedChapter($"Chapter {i + 1:D3}  - 00:06:00", TimeSpan.FromMinutes(6) * i, TimeSpan.FromMinutes(6) * (i + 1)))
                .ToList();
            GivenFileReads(marks, new ChapterAtomState(true, null, 5, true));
            GivenHeard(marks, new Dictionary<int, string> { [1] = "Chapter one.", [2] = "Chapter two.", [3] = "Chapter three.", [4] = "Chapter four." });

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterHealth.GenericTitles, file.Health);
            Assert.True(file.Repairable);
            Assert.Equal(5, file.Plan!.Chapters.Count);
            Assert.Equal(["Introduction", "Chapter 1", "Chapter 2", "Chapter 3", "Chapter 4"], file.Plan.Chapters.Select(c => c.Title));
        }

        [Fact]
        public async Task PreviewAsync_MergesPlaceholderMarksWhenOnlyAFewAreAnnounced()
        {
            // A War of Gifts: 25 five-minute tracks titled "Chapter 001 - 00:00:38", with
            // the author's chapters heard at a handful of them.
            GivenBook();
            GivenTranscription(enabled: true);
            var marks = Enumerable.Range(0, 25)
                .Select(i => new EmbeddedChapter($"Chapter {i + 1:D3}  - 00:05:00", TimeSpan.FromMinutes(3) * i, TimeSpan.FromMinutes(3) * (i + 1)))
                .ToList();
            GivenFileReads(marks, new ChapterAtomState(true, null, 25, true));
            GivenHeard(marks, new Dictionary<int, string>
            {
                [1] = "1. Saint Nick\nZach Morgan sat attentively on the front row.",
                [5] = "2. Ender's stocking\nPeter Wiggin was supposed to spend the day at the library.",
                [7] = "3. The Devil's Questions\nZack got into a hover car with the man."
            });

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.True(file.Repairable);
            Assert.Equal(
                ["Introduction", "Chapter 1: Saint Nick", "Chapter 2: Ender's stocking", "Chapter 3: The Devil's Questions"],
                file.Plan!.Chapters.Select(c => c.Title));
            Assert.Equal(marks[1].Start, file.Plan.Chapters[1].Start);
        }

        private void GivenAudnexus(IReadOnlyList<EmbeddedChapter> chapters, TimeSpan runtime) =>
            _audnexus
                .Setup(a => a.GetChaptersAsync("B00X", It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new AudnexusChapterResponse
                {
                    RuntimeLengthMs = (int)runtime.TotalMilliseconds,
                    Chapters = chapters.Select(c => new AudnexusChapter
                    {
                        Title = c.Title,
                        StartOffsetMs = (int)c.Start.TotalMilliseconds,
                        LengthMs = (int)(c.End - c.Start).TotalMilliseconds
                    }).ToList()
                });

        [Fact]
        public async Task PreviewAsync_UsesTheEditionsMarksWithoutListeningWhenTheyLandOnTheRips()
        {
            // Transcription off, but Audible's chapters sit on every third track: the
            // edition is this file and its list is the plan, at no cost.
            GivenBook(asin: "B00X");
            var marks = Tracks(30, TimeSpan.FromMinutes(3));
            GivenFileReads(marks, new ChapterAtomState(true, null, 30, true));
            GivenAudnexus(
                Enumerable.Range(0, 10).Select(i => new EmbeddedChapter($"Chapter {i + 1}", TimeSpan.FromMinutes(9) * i + TimeSpan.FromMilliseconds(400), TimeSpan.FromMinutes(9) * (i + 1))).ToList(),
                Duration - TimeSpan.FromSeconds(10));

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.True(file.Repairable);
            Assert.Equal(ChapterSource.Audnexus, file.Plan!.Source);
            Assert.Equal(10, file.Plan.Chapters.Count);
            // Snapped to the rip's own mark, not Audible's rounded offset.
            Assert.Equal(TimeSpan.FromMinutes(9), file.Plan.Chapters[1].Start);
            Assert.Contains("10 of them on the file's own marks", file.Plan.Note);
            _transcriber.Verify(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PreviewAsync_ListensOnlyAtTheEditionsMarksForTheirNames()
        {
            GivenBook(asin: "B00X");
            GivenTranscription(enabled: true);
            var marks = Tracks(30, TimeSpan.FromMinutes(3));
            GivenFileReads(marks, new ChapterAtomState(true, null, 30, true));
            GivenAudnexus(
                Enumerable.Range(0, 10).Select(i => new EmbeddedChapter($"Chapter {i + 1}", TimeSpan.FromMinutes(9) * i, TimeSpan.FromMinutes(9) * (i + 1))).ToList(),
                Duration);
            GivenHeard(marks, new Dictionary<int, string> { [3] = "2. Stockings.\nRat Army was small.", [6] = "3. Peace." });

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterSource.Audnexus, file.Plan!.Source);
            Assert.Equal("Chapter 2: Stockings", file.Plan.Chapters[1].Title);
            Assert.Equal("Chapter 3: Peace", file.Plan.Chapters[2].Title);
            Assert.Equal("Chapter 4", file.Plan.Chapters[3].Title);
            // Ten marks, not thirty. (The mark after each would be listened to as well
            // were it within the lead-in window; these tracks are three minutes apart.)
            _transcriber.Verify(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(10));
        }

        [Fact]
        public async Task PreviewAsync_IgnoresAnEditionWhoseMarksDoNotLandOnTheRips()
        {
            // Same runtime, different cut: the edition's marks fall between the tracks.
            GivenBook(asin: "B00X");
            GivenTranscription(enabled: true);
            var marks = Tracks(30, TimeSpan.FromMinutes(3));
            GivenFileReads(marks, new ChapterAtomState(true, null, 30, true));
            GivenAudnexus(
                Enumerable.Range(0, 10).Select(i => new EmbeddedChapter($"Chapter {i + 1}", TimeSpan.FromMinutes(9) * i + TimeSpan.FromSeconds(70), TimeSpan.FromMinutes(9) * (i + 1))).ToList(),
                Duration);
            GivenHeard(marks, new Dictionary<int, string> { [0] = "Chapter one.", [15] = "Chapter two." });

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterSource.Announcements, file.Plan!.Source);
            Assert.Equal(2, file.Plan.Chapters.Count);
        }

        [Fact]
        public async Task PreviewAsync_NamesAShortWorkFromItsCreditsWhenNothingIsAnnounced()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            var marks = new List<EmbeddedChapter>
            {
                new("Chapter 001  - 00:00:09", TimeSpan.Zero, TimeSpan.FromSeconds(9)),
                new("Chapter 002  - 01:05:16", TimeSpan.FromSeconds(9), TimeSpan.FromMinutes(65)),
                new("Chapter 003  - 00:54:33", TimeSpan.FromMinutes(65), TimeSpan.FromMinutes(99)),
                new("Chapter 004  - 00:00:25", TimeSpan.FromMinutes(99), TimeSpan.FromMinutes(99.5))
            };
            GivenFileReads(marks, new ChapterAtomState(true, null, 4, true));
            GivenHeard(marks, new Dictionary<int, string>
            {
                [0] = "Macmillan Audio presents Unauthorized Bread by Cory Doctorow.\nRead for you by Lameece Issaq.",
                [3] = "We hope you've enjoyed Unauthorized Bread, a Macmillan audio production."
            });

            var preview = await BuildService().PreviewAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.True(file.Repairable);
            Assert.Equal(ChapterSource.Credits, file.Plan!.Source);
            Assert.Equal(["Intro", "Part 1", "Part 2", "Credits"], file.Plan.Chapters.Select(c => c.Title));
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
