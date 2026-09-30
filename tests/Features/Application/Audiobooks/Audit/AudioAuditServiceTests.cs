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
using Listenarr.Application.Audiobooks.Audit;
using Listenarr.Application.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Application.Audiobooks.Transcription;
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Audit
{
    [Trait("Name", "AudioAuditServiceTests")]
    [Trait("Category", "Tagging")]
    public sealed class AudioAuditServiceTests : BaseTests
    {
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly Mock<ITagQueueService> _queue = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IFileSystem> _fileSystem = new();
        private readonly Mock<ITranscriber> _transcriber = new();

        private AudioAuditService BuildService() => new(
            _audiobooks.Object,
            _queue.Object,
            _configuration.Object,
            _fileSystem.Object,
            NullLogger<AudioAuditService>.Instance,
            _transcriber.Object,
            new TranscriptCache());

        private ApplicationSettings GivenSettings(bool transcription, bool onImport = false)
        {
            var settings = new ApplicationSettingsBuilder().Build();
            settings.TranscriptionEnabled = transcription;
            settings.AudioAuditOnImport = onImport;
            _configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);
            _transcriber.Setup(t => t.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transcription);
            return settings;
        }

        private Audiobook GivenBook(params (string Path, double? Seconds)[] files)
        {
            var audiobook = new AudiobookBuilder().WithId(7).WithTitle("A War of Gifts").WithBasePath("/library/book").Build();
            audiobook.Authors = ["Orson Scott Card"];
            audiobook.Narrators = ["Scott Brick"];
            audiobook.Files = files.Select((f, i) =>
            {
                var file = AudiobookFile.CreateUnresolved(f.Path);
                file.Id = 40 + i;
                file.DurationSeconds = f.Seconds;
                return file;
            }).ToList();
            _audiobooks.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(audiobook);
            _fileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
            _fileSystem.Setup(fs => fs.GetFileLength(It.IsAny<string>())).Returns(1024);
            _fileSystem.Setup(fs => fs.GetLastWriteTimeUtc(It.IsAny<string>())).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            return audiobook;
        }

        private void GivenHeard(string opening, string closing)
        {
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), TimeSpan.Zero, AudioAuditService.OpeningWindow, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript(opening));
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.Is<TimeSpan>(s => s > TimeSpan.Zero), AudioAuditService.ClosingWindow, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript(closing));
        }

        [Fact]
        public async Task EnqueueAsync_RefusesWhileTranscriptionIsOff()
        {
            GivenSettings(transcription: false);

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Manual);

            Assert.Equal(TagEnqueueOutcome.Disabled, result.Outcome);
            _queue.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task EnqueueAsync_AutomaticNeedsTheImportSetting()
        {
            GivenSettings(transcription: true, onImport: false);

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Automatic);

            Assert.Equal(TagEnqueueOutcome.Disabled, result.Outcome);
            _queue.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task EnqueueAsync_HandsAManualRequestToTheQueue()
        {
            GivenSettings(transcription: true);
            _queue
                .Setup(q => q.EnqueueAudioAuditAsync(7, TagTrigger.Manual, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TagEnqueueResult(TagEnqueueOutcome.Queued, Guid.NewGuid()));

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Manual);

            Assert.Equal(TagEnqueueOutcome.Queued, result.Outcome);
        }

        [Fact]
        public async Task AuditAsync_HearsTheOpeningOfTheFirstFileAndTheClosingOfTheLast()
        {
            GivenSettings(transcription: true);
            GivenBook(("Part 1.m4b", 3600), ("Part 2.m4b", 2400));
            GivenHeard(
                "A War of Gifts, by Orson Scott Card. Read by Scott Brick.\n1. Saint Nick\nZach Morgan sat attentively on the front row.",
                "This has been A War of Gifts by Orson Scott Card. Read by Scott Brick.");
            AudioAuditRecord? stored = null;
            _audiobooks
                .Setup(r => r.SetAudioAuditAsync(7, It.IsAny<AudioAuditRecord>(), It.IsAny<CancellationToken>()))
                .Callback<int, AudioAuditRecord, CancellationToken>((_, record, _) => stored = record)
                .Returns(Task.CompletedTask);

            var result = await BuildService().AuditAsync(7);

            Assert.Equal(AudioAuditVerdict.Match, result.Verdict);
            Assert.Equal(AudioAuditVerdict.Match, stored!.Verdict);
            Assert.Equal("Scott Brick", stored.Credits.Narrator);
            Assert.Contains("Saint Nick", stored.Heard);
            _transcriber.Verify(
                t => t.TranscribeAsync(It.Is<string>(p => p.EndsWith("Part 1.m4b")), TimeSpan.Zero, AudioAuditService.OpeningWindow, It.IsAny<CancellationToken>()),
                Times.Once);
            // No chapters known for the last file, so the closing is the last stretch.
            _transcriber.Verify(
                t => t.TranscribeAsync(It.Is<string>(p => p.EndsWith("Part 2.m4b")), TimeSpan.FromSeconds(2400) - AudioAuditService.ClosingWindow, AudioAuditService.ClosingWindow, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task AuditAsync_HearsAShortFinalChapterFromItsStart()
        {
            // Drive: the credits are read at the top of a 104-second last chapter, not
            // in the last ninety seconds of the file.
            GivenSettings(transcription: true);
            GivenBook(("Drive.m4b", 3469));
            var writer = new Mock<IAudiobookTagWriter>();
            writer
                .Setup(w => w.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AudiobookFileTags(
                    new Dictionary<string, string>(),
                    3,
                    TimeSpan.FromSeconds(3469),
                    false,
                    Chapters:
                    [
                        new EmbeddedChapter("1", TimeSpan.Zero, TimeSpan.FromSeconds(29)),
                        new EmbeddedChapter("2", TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(3364.758)),
                        new EmbeddedChapter("3", TimeSpan.FromSeconds(3364.758), TimeSpan.FromSeconds(3469))
                    ]));
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, TimeSpan start, TimeSpan _, CancellationToken _) =>
                    new Transcript(start == TimeSpan.Zero
                        ? "Drive. An Expanse short story. Chapter one. Solomon Epstein had built his little yacht."
                        : "This has been a Hachette Audio production of Drive, an Expanse short story, by James S. A. Corey."));

            var service = new AudioAuditService(
                _audiobooks.Object,
                _queue.Object,
                _configuration.Object,
                _fileSystem.Object,
                NullLogger<AudioAuditService>.Instance,
                _transcriber.Object,
                new TranscriptCache(),
                writer.Object);
            var result = await service.AuditAsync(7);

            // The whole of the credits chapter, not the first ninety seconds of it. This
            // one runs 104s; capping it left the last fourteen seconds unheard, and on a
            // real book - The Lost World, whose final chapter is 161s - it stopped the
            // transcript 71 seconds early, mid-sentence, which the completeness check then
            // read as the book being cut off.
            _transcriber.Verify(
                t => t.TranscribeAsync(
                    It.IsAny<string>(),
                    TimeSpan.FromSeconds(3364.758),
                    TimeSpan.FromSeconds(3469) - TimeSpan.FromSeconds(3364.758),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.Equal("Drive, an Expanse short story", result.Credits.Title);
            Assert.Equal("James S. A. Corey", result.Credits.Author);
        }

        [Fact]
        public async Task AuditAsync_RecordsAMismatchWithWhatWasHeard()
        {
            GivenSettings(transcription: true);
            GivenBook(("Book.m4b", 3600));
            GivenHeard(
                "The Forever War, by Joe Haldeman. Narrated by George Wilson. Private Mandella, report to the briefing room at once.",
                "[Music]");
            AudioAuditRecord? stored = null;
            _audiobooks
                .Setup(r => r.SetAudioAuditAsync(7, It.IsAny<AudioAuditRecord>(), It.IsAny<CancellationToken>()))
                .Callback<int, AudioAuditRecord, CancellationToken>((_, record, _) => stored = record)
                .Returns(Task.CompletedTask);

            var result = await BuildService().AuditAsync(7);

            Assert.Equal(AudioAuditVerdict.Mismatch, result.Verdict);
            Assert.Equal("The Forever War", stored!.Credits.Title);
            Assert.Equal("Joe Haldeman", stored.Credits.Author);
        }

        [Fact]
        public async Task AuditAsync_SkipsTheClosingWhenTheDurationIsUnknown()
        {
            GivenSettings(transcription: true);
            GivenBook(("Book.mp3", null));
            GivenHeard("A War of Gifts by Orson Scott Card. Zach Morgan sat attentively on the front row of the sanctuary.", "unused");

            var result = await BuildService().AuditAsync(7);

            Assert.Equal(AudioAuditVerdict.Match, result.Verdict);
            _transcriber.Verify(
                t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
        // ---- the windows must cover the file ------------------------------------------
        //
        // Everything else in this suite starts from a transcript that already exists. These
        // are about the step before that: which stretches of audio get listened to at all.
        // Both bugs they cover shipped, because nothing asserted the coverage.

        [Theory]
        [Trait("Method", "AuditAsync")]
        [Trait("Scenario", "ClosingWindowReachesTheEnd")]
        [InlineData(30)]
        [InlineData(104)]
        [InlineData(161)]
        [InlineData(600)]
        public async Task AuditAsync_ClosingWindowAlwaysReachesTheEndOfTheFile(double finalChapterSeconds)
        {
            // A credits chapter shorter than three minutes moves the window's start. It
            // must never shorten its reach: The Lost World's final chapter runs 161s, the
            // window took only the first 90 of them, and the transcript stopped 71 seconds
            // early in the middle of a sentence.
            const double duration = 3600;
            GivenSettings(transcription: true);
            GivenBook(("Book.m4b", duration));
            GivenHeard("A War of Gifts by Orson Scott Card, read by Scott Brick. Chapter one.", "This has been A War of Gifts.");

            var writer = new Mock<IAudiobookTagWriter>();
            writer
                .Setup(w => w.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AudiobookFileTags(
                    new Dictionary<string, string>(),
                    1,
                    TimeSpan.FromSeconds(duration),
                    false,
                    Chapters:
                    [
                        new EmbeddedChapter("1", TimeSpan.Zero, TimeSpan.FromSeconds(duration - finalChapterSeconds)),
                        new EmbeddedChapter("2", TimeSpan.FromSeconds(duration - finalChapterSeconds), TimeSpan.FromSeconds(duration))
                    ]));

            TimeSpan? start = null;
            TimeSpan? window = null;
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.Is<TimeSpan>(s => s > TimeSpan.Zero), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Callback((string _, TimeSpan s, TimeSpan w, CancellationToken _) => { start = s; window = w; })
                .ReturnsAsync(new Transcript("This has been A War of Gifts."));

            var service = new AudioAuditService(
                _audiobooks.Object,
                _queue.Object,
                _configuration.Object,
                _fileSystem.Object,
                NullLogger<AudioAuditService>.Instance,
                _transcriber.Object,
                new TranscriptCache(),
                writer.Object);
            await service.AuditAsync(7);

            Assert.NotNull(start);
            Assert.NotNull(window);
            Assert.Equal(duration, start!.Value.TotalSeconds + window!.Value.TotalSeconds, 1);
        }

        [Fact]
        [Trait("Method", "AuditAsync")]
        [Trait("Scenario", "RetryKeepsWhatWasAlreadyHeard")]
        public async Task AuditAsync_ListeningAgainKeepsWhatTheFirstPassHeard()
        {
            // When the opening says nothing about the book the audit listens again from
            // later in the file. What it already heard is kept: a transcript that merely
            // starts a few seconds in is how 2001: A Space Odyssey lost its title.
            GivenSettings(transcription: true);
            GivenBook(("Book.m4b", 3600));

            const string firstPass = "1.\nHealers Dolores never met a healer she didn't like.";
            const string secondPass = "Blackstone Audio presents A War of Gifts, written by Orson Scott Card, read by Scott Brick.";
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), TimeSpan.Zero, AudioAuditService.OpeningWindow, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript(firstPass));
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.Is<TimeSpan>(s => s > TimeSpan.Zero && s < TimeSpan.FromSeconds(60)), AudioAuditService.OpeningWindow, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript(secondPass));
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.Is<TimeSpan>(s => s > TimeSpan.FromSeconds(60)), AudioAuditService.ClosingWindow, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript("The end."));

            var silences = new Mock<ISilenceDetector>();
            silences
                .Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new SilenceSpan(TimeSpan.FromSeconds(1.7), TimeSpan.FromSeconds(3.2))]);

            AudioAuditRecord? saved = null;
            _audiobooks
                .Setup(r => r.SetAudioAuditAsync(7, It.IsAny<AudioAuditRecord>(), It.IsAny<CancellationToken>()))
                .Callback((int _, AudioAuditRecord record, CancellationToken _) => saved = record)
                .Returns(Task.CompletedTask);

            var service = new AudioAuditService(
                _audiobooks.Object,
                _queue.Object,
                _configuration.Object,
                _fileSystem.Object,
                NullLogger<AudioAuditService>.Instance,
                _transcriber.Object,
                new TranscriptCache(),
                null,
                silences.Object);
            await service.AuditAsync(7);

            Assert.NotNull(saved);
            Assert.Contains("Blackstone Audio presents", saved!.Heard);
            Assert.Contains("Healers Dolores", saved.Heard);
        }

        [Fact]
        [Trait("Method", "AuditAsync")]
        [Trait("Scenario", "WordsFromTheSameOffsetAreNotStacked")]
        public async Task AuditAsync_DoesNotKeepWordsCoveringTheSameAudio()
        {
            // The words on record were taken from the same offset this listen starts at, so
            // they cover the same audio and this listen has just covered it better. Keeping
            // them put three copies of The Barsoom Project's prologue on its record.
            var settings = GivenSettings(transcription: true);
            var model = ChapterPlanKeys.ModelFor(settings.TranscriptionEnabled, settings.TranscriptionModel);
            var book = GivenBook(("Book.m4b", 3600));
            book.AudioAuditHeard = "Like a raging mountain, the Terrichik rose screaming from a frozen night-dark sea.";
            book.AudioAuditFileIdentity = AudioAuditFileIdentity.Of(
                [(4096L, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))], model, 2.7);

            const string again = "Like a raging mountain, the Terri-Chick rose screaming from a frozen night-dark sea.";
            // The opening and closing windows are the same length, so the start is what tells
            // the two calls apart.
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.Is<TimeSpan>(v => v < TimeSpan.FromSeconds(60)), AudioAuditService.OpeningWindow, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript(again));
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.Is<TimeSpan>(v => v > TimeSpan.FromSeconds(60)), AudioAuditService.ClosingWindow, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript("The end."));

            AudioAuditRecord? saved = null;
            _audiobooks
                .Setup(r => r.SetAudioAuditAsync(7, It.IsAny<AudioAuditRecord>(), It.IsAny<CancellationToken>()))
                .Callback((int _, AudioAuditRecord record, CancellationToken _) => saved = record)
                .Returns(Task.CompletedTask);

            await BuildService().AuditAsync(7);

            Assert.NotNull(saved);
            Assert.Contains("Terri-Chick", saved!.Heard);
            Assert.DoesNotContain("Terrichik", saved.Heard);
        }

        [Fact]
        [Trait("Method", "AuditAsync")]
        [Trait("Scenario", "ARecordedSkipCostsNothingToReJudge")]
        public async Task AuditAsync_ReUsesATranscriptTakenPastTheIdent()
        {
            // The whole point of the identity: a re-run after the credits parser learns
            // something re-judges the words on record for free. A book already heard past
            // its shop ident was the one case that did not, because the skip was worked out
            // again as zero and the identity it built could never equal the stored one. It
            // cost two transcriptions a book, on every audit, to arrive back where it was.
            var settings = GivenSettings(transcription: true);
            var model = ChapterPlanKeys.ModelFor(settings.TranscriptionEnabled, settings.TranscriptionModel);
            var book = GivenBook(("Book.m4b", 3600));
            book.AudioAuditHeard = "Blackstone Audio presents A War of Gifts, written by Orson Scott Card, read by Scott Brick.";
            book.AudioAuditFileIdentity = AudioAuditFileIdentity.Of(
                [(1024L, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))], model, 2.6);

            AudioAuditRecord? saved = null;
            _audiobooks
                .Setup(r => r.SetAudioAuditAsync(7, It.IsAny<AudioAuditRecord>(), It.IsAny<CancellationToken>()))
                .Callback((int _, AudioAuditRecord record, CancellationToken _) => saved = record)
                .Returns(Task.CompletedTask);

            await BuildService().AuditAsync(7);

            _transcriber.Verify(
                t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.NotNull(saved);
            Assert.Equal(book.AudioAuditHeard, saved!.Heard);
            Assert.Equal(book.AudioAuditFileIdentity, saved.FileIdentity);
        }

        [Fact]
        [Trait("Method", "AuditAsync")]
        [Trait("Scenario", "ASecondListenThatFoundNothingIsStillRecorded")]
        public async Task AuditAsync_RecordsTheSkipEvenWhenTheSecondListenFindsNothing()
        {
            // A listen that came back with no credits is still a listen that happened, and
            // it is expensive. Left off the identity it would be attempted again on every
            // audit of a book already known to have nothing there.
            GivenSettings(transcription: true);
            GivenBook(("Book.m4b", 3600));

            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), TimeSpan.Zero, AudioAuditService.OpeningWindow, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript("1.\nHealers Dolores never met a healer she didn't like."));
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.Is<TimeSpan>(s => s > TimeSpan.Zero && s < TimeSpan.FromSeconds(60)), AudioAuditService.OpeningWindow, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript("never met a healer she didn't like, and this one was no exception."));
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.Is<TimeSpan>(s => s > TimeSpan.FromSeconds(60)), AudioAuditService.ClosingWindow, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript("The end."));

            var silences = new Mock<ISilenceDetector>();
            silences
                .Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new SilenceSpan(TimeSpan.FromSeconds(1.7), TimeSpan.FromSeconds(3.2))]);

            AudioAuditRecord? saved = null;
            _audiobooks
                .Setup(r => r.SetAudioAuditAsync(7, It.IsAny<AudioAuditRecord>(), It.IsAny<CancellationToken>()))
                .Callback((int _, AudioAuditRecord record, CancellationToken _) => saved = record)
                .Returns(Task.CompletedTask);

            var service = new AudioAuditService(
                _audiobooks.Object,
                _queue.Object,
                _configuration.Object,
                _fileSystem.Object,
                NullLogger<AudioAuditService>.Instance,
                _transcriber.Object,
                new TranscriptCache(),
                null,
                silences.Object);
            await service.AuditAsync(7);

            Assert.NotNull(saved);

            // The words kept are the ones from the start; the skip is on the record anyway.
            Assert.Contains("Healers Dolores", saved!.Heard);
            Assert.True(
                AudioAuditFileIdentity.OpeningSkipOf(saved.FileIdentity) > TimeSpan.Zero,
                $"the skip that was tried is not on the identity: {saved.FileIdentity}");
        }
    }
}
