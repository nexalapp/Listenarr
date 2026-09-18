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

        private void GivenSettings(bool transcription, bool onImport = false)
        {
            var settings = new ApplicationSettingsBuilder().Build();
            settings.TranscriptionEnabled = transcription;
            settings.AudioAuditOnImport = onImport;
            _configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);
            _transcriber.Setup(t => t.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transcription);
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
            _transcriber.Verify(
                t => t.TranscribeAsync(It.Is<string>(p => p.EndsWith("Part 2.m4b")), TimeSpan.FromSeconds(2400) - AudioAuditService.ClosingWindow, AudioAuditService.ClosingWindow, It.IsAny<CancellationToken>()),
                Times.Once);
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
    }
}
