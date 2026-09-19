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
using Listenarr.Infrastructure.Library.Transcription;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Transcription
{
    /// <summary>
    /// The native whisper path, end to end: ffmpeg cuts and resamples, whisper.cpp
    /// listens. Runs only where a model is present (see <see cref="WhisperFactAttribute"/>);
    /// its value is proving the native libraries load and run on this platform.
    /// </summary>
    [Trait("Name", "WhisperTranscriberTests")]
    [Trait("Category", "Tagging")]
    public sealed class WhisperTranscriberTests : BaseTests, IDisposable
    {
        private readonly string _workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "listenarr-whisper-" + Guid.NewGuid().ToString("N"));

        private WhisperTranscriber Build(bool enabled)
        {
            var settings = new ApplicationSettingsBuilder().Build();
            settings.TranscriptionEnabled = enabled;
            settings.TranscriptionModel = "base.en";

            var configuration = new Mock<IConfigurationService>();
            configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);

            var services = new ServiceCollection();
            services.AddSingleton(configuration.Object);
            services.AddSingleton(M4bFixtures.PathResolvedFfmpeg());
            services.AddSingleton<IProcessRunner>(new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance));
            var provider = services.BuildServiceProvider();

            var paths = new Mock<IApplicationPathService>();
            paths.Setup(p => p.ResolveFromConfig("whisper"))
                .Returns(Environment.GetEnvironmentVariable(WhisperFactAttribute.ModelsVariable) ?? _workingDirectory);

            return new WhisperTranscriber(
                provider.GetRequiredService<IServiceScopeFactory>(),
                paths.Object,
                NullLogger<WhisperTranscriber>.Instance);
        }

        [Fact]
        public async Task IsAvailableAsync_IsFalseWhileTranscriptionIsOff()
        {
            // Off means off: no model is fetched, nothing is loaded.
            using var transcriber = Build(enabled: false);
            Assert.False(await transcriber.IsAvailableAsync());
            Assert.False(Directory.Exists(Path.Combine(_workingDirectory, "ggml-base.en.bin")));
        }

        [Fact]
        public async Task GetModelStatusAsync_ReportsAModelThatIsNotThereWithoutFetchingIt()
        {
            using var transcriber = Build(enabled: true);

            var status = await transcriber.GetModelStatusAsync();

            Assert.Equal("base.en", status.Model);
            Assert.Equal(TranscriptionModelState.Missing, status.State);
            Assert.False(File.Exists(Path.Combine(_workingDirectory, "ggml-base.en.bin")));
        }

        [Fact]
        public async Task IsAvailableAsync_IsTrueOnceTheModelIsOnDisk()
        {
            // Only the bytes on disk matter; the settings page shows the size.
            Directory.CreateDirectory(_workingDirectory);
            await File.WriteAllBytesAsync(Path.Combine(_workingDirectory, "ggml-base.en.bin"), new byte[64]);
            using var transcriber = Build(enabled: true);

            Assert.True(await transcriber.IsAvailableAsync());
            var status = await transcriber.GetModelStatusAsync("base.en");
            Assert.Equal(TranscriptionModelState.Ready, status.State);
            Assert.Equal(64, status.SizeBytes);
        }

        [Fact]
        public async Task GetModelStatusAsync_NamesAModelItDoesNotKnow()
        {
            using var transcriber = Build(enabled: true);

            var status = await transcriber.GetModelStatusAsync("large-v3");

            Assert.Equal(TranscriptionModelState.Failed, status.State);
            Assert.Contains("not a whisper model", status.Error);
        }

        [WhisperFact]
        public async Task TranscribeAsync_HearsSilenceAsNothingAndDoesNotThrow()
        {
            var path = await M4bFixtures.WriteBookAsync(_workingDirectory, seconds: 6);
            using var transcriber = Build(enabled: true);

            Assert.True(await transcriber.IsAvailableAsync());
            var transcript = await transcriber.TranscribeAsync(path, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));

            // A sine tone is not speech. Whatever whisper hallucinates for it must not
            // parse as a chapter, which is the property the planner relies on.
            Assert.Null(Listenarr.Domain.Audiobooks.Chapters.ChapterAnnouncementParser.Parse(transcript.Text));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_workingDirectory))
                {
                    Directory.Delete(_workingDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
