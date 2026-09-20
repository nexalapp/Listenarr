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
using Listenarr.Application.Audiobooks.Transcription;
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Domain.FoundBooks;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.FoundBooks
{
    [Trait("Name", "FoundBookMatchServiceTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookMatchServiceTests : BaseTests
    {
        private readonly Mock<ITranscriber> _transcriber = new();
        private IFoundBookRepository _repository = null!;
        private IFileSystem _fileSystem = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _repository = _provider.GetRequiredService<IFoundBookRepository>();
            _fileSystem = _provider.GetRequiredService<IFileSystem>();
        }

        private FoundBookMatchService BuildService(bool withTranscriber = true) => new(
            _repository,
            _provider.GetRequiredService<IConfigurationService>(),
            _fileSystem,
            TimeProvider.System,
            NullLogger<FoundBookMatchService>.Instance,
            withTranscriber ? _transcriber.Object : null,
            new TranscriptCache());

        private async Task<FoundBook> GivenRow(string? audioPath = null)
        {
            var entries = audioPath == null
                ? Array.Empty<FoundBookFileEntry>()
                : [new FoundBookFileEntry(audioPath, new FileInfo(audioPath).Length, File.GetLastWriteTimeUtc(audioPath), true, null)];
            return await _repository.AddAsync(new FoundBook
            {
                ClusterKey = Guid.NewGuid().ToString("N"),
                Signature = "sig",
                WatchFolder = FileService.GetTempPath(),
                BookFolder = FileService.GetTempPath(),
                FilesJson = FoundBookFilesJson.Serialize(entries),
                AudioFileCount = entries.Length,
                DetectedTitle = "5da8cead8779ae4719256549"
            });
        }

        private async Task GivenTranscription(bool enabled)
        {
            var settings = await _applicationSettingsRepository.GetAsync() ?? new ApplicationSettings();
            settings.TranscriptionEnabled = enabled;
            await _applicationSettingsRepository.SaveAsync(settings);
        }

        [Fact]
        public async Task SetMatch_IsRememberedOnTheRow_AndClearedByNull()
        {
            var row = await GivenRow();
            var service = BuildService();

            var set = await service.SetMatchAsync(row.Id, new FoundBookMatchChoice("B00ABCDEF1", "Fearless", "Jack Campbell", "Audible", "http://img", 0.97));
            Assert.Equal("B00ABCDEF1", set!.MatchAsin);
            Assert.Equal("Fearless", set.MatchTitle);
            Assert.Equal(0.97, set.MatchConfidence);

            var cleared = await service.SetMatchAsync(row.Id, null);
            Assert.Null(cleared!.MatchAsin);
            Assert.Null(cleared.MatchConfidence);
        }

        [Fact]
        public async Task SetMatch_ClampsConfidence()
        {
            var row = await GivenRow();
            var set = await BuildService().SetMatchAsync(row.Id, new FoundBookMatchChoice("B00ABCDEF1", "Fearless", null, null, null, 7));
            Assert.Equal(1, set!.MatchConfidence);
        }

        [Fact]
        public async Task Listen_HearsTheOpeningCreditsAndKeepsThem()
        {
            await GivenTranscription(true);
            var audio = await FileService.GetTempFileAsync("part1.mp3");
            var row = await GivenRow(audio);
            _transcriber.Setup(t => t.IsAvailableAsync(TranscriptionPolicy.Always, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _transcriber
                .Setup(t => t.TranscribeAsync(audio, TimeSpan.Zero, It.IsAny<TimeSpan>(), TranscriptionPolicy.Always, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript("Fearless, by Jack Campbell. Read by Christian Rummel."));

            var heard = await BuildService().ListenAsync(row.Id);

            Assert.NotNull(heard);
            Assert.Equal("Fearless", heard!.Credits.Title);
            Assert.Equal("Jack Campbell", heard.Credits.Author);
            Assert.Equal("Christian Rummel", heard.Credits.Narrator);
            var after = (await _repository.GetAsync(row.Id))!;
            Assert.Equal("Fearless", after.HeardTitle);
            Assert.Equal("Jack Campbell", after.HeardAuthor);
            Assert.NotNull(after.HeardAt);
        }

        [Fact]
        public async Task Listen_ListensEvenWhenTheTranscriptionSettingIsOff()
        {
            // The setting governs listening to every scanned book. Identifying a found
            // book is the Found tab's whole promise, so it listens regardless.
            await GivenTranscription(false);
            var audio = await FileService.GetTempFileAsync("part1.mp3");
            var row = await GivenRow(audio);
            _transcriber.Setup(t => t.IsAvailableAsync(TranscriptionPolicy.Always, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _transcriber
                .Setup(t => t.TranscribeAsync(audio, TimeSpan.Zero, It.IsAny<TimeSpan>(), TranscriptionPolicy.Always, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Transcript("Fearless, by Jack Campbell."));

            var heard = await BuildService().ListenAsync(row.Id);

            Assert.Equal("Fearless", heard!.Credits.Title);
            _transcriber.Verify(
                t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Listen_SaysSoWhileTheModelDownloads()
        {
            await GivenTranscription(true);
            var audio = await FileService.GetTempFileAsync("part1.mp3");
            var row = await GivenRow(audio);
            _transcriber.Setup(t => t.IsAvailableAsync(TranscriptionPolicy.Always, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            await Assert.ThrowsAsync<TranscriptionUnavailableException>(() => BuildService().ListenAsync(row.Id));
        }
    }
}
