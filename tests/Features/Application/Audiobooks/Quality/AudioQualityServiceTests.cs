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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Domain.Audiobooks.Quality;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Quality
{
    [Trait("Name", "AudioQualityServiceTests")]
    [Trait("Category", "Tagging")]
    public sealed class AudioQualityServiceTests : BaseTests
    {
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly Mock<ITagQueueService> _queue = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IAudioSampleReader> _reader = new();
        private readonly Mock<IFileSystem> _fileSystem = new();

        public AudioQualityServiceTests()
        {
            _configuration.Setup(c => c.GetApplicationSettingsAsync())
                .ReturnsAsync(new ApplicationSettingsBuilder().Build());
            _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        }

        private AudioQualityService BuildService() => new(
            _audiobooks.Object,
            _queue.Object,
            _configuration.Object,
            _reader.Object,
            _fileSystem.Object,
            NullLogger<AudioQualityService>.Instance);

        private Audiobook GivenBook(params (string Path, double? Seconds)[] files)
        {
            var audiobook = new AudiobookBuilder().WithId(7).WithTitle("Ender's Shadow").WithBasePath("/library/book").Build();
            audiobook.Files = files.Select((f, i) =>
            {
                var file = AudiobookFile.CreateUnresolved(f.Path);
                file.Id = 40 + i;
                file.DurationSeconds = f.Seconds;
                return file;
            }).ToList();
            _audiobooks.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(audiobook);
            return audiobook;
        }

        /// <summary>A window the probe reads as speech: harmonic bursts over near-silence.</summary>
        private static float[] SpeechWindow()
        {
            var samples = new float[44_100 * 12];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / 44_100.0;
                if (t % 0.6 < 0.35)
                {
                    double v = 0;
                    for (var h = 1; h <= 40; h++)
                    {
                        v += 0.3 / h * Math.Sin(2 * Math.PI * 140 * h * t);
                    }

                    samples[i] = (float)(v * 0.5);
                }
            }

            return samples;
        }

        [Fact]
        public async Task Probe_StoresTheVerdictPinnedToTheFiles()
        {
            GivenBook(("a.mp3", 3600), ("b.mp3", 1800));
            _reader.Setup(r => r.ReadWindowsAsync("/library/book/a.mp3", 3600, It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AudioSampleWindows([SpeechWindow(), SpeechWindow(), SpeechWindow()], 44_100));

            AudioQualityRecord? stored = null;
            _audiobooks.Setup(r => r.SetAudioQualityAsync(7, It.IsAny<AudioQualityRecord>(), It.IsAny<CancellationToken>()))
                .Callback<int, AudioQualityRecord, CancellationToken>((_, record, _) => stored = record)
                .Returns(Task.CompletedTask);

            var report = await BuildService().ProbeAsync(7);

            Assert.NotEqual(AudioQuality.Unknown, report.Verdict);
            Assert.NotNull(stored);
            Assert.Equal(report.Verdict, stored!.Verdict);
            // Pinned to every audio file's duration and the probe's version, so a changed
            // file or a smarter probe re-opens the verdict.
            Assert.Equal(AudioQualityIdentity.Of([3600, 1800]), stored.FileIdentity);
            Assert.Contains("\"NoiseFloorDb\"", stored.MetricsJson);
        }

        /// <summary>The longest file carries the recording; a short companion would judge the intro.</summary>
        [Fact]
        public async Task Probe_ReadsTheLongestFile()
        {
            GivenBook(("intro.mp3", 90), ("book.mp3", 7200));
            _reader.Setup(r => r.ReadWindowsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AudioSampleWindows([SpeechWindow()], 44_100));

            await BuildService().ProbeAsync(7);

            _reader.Verify(
                r => r.ReadWindowsAsync("/library/book/book.mp3", 7200, It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// An undecodable file stores an honest Unknown rather than failing: a thrown
        /// probe stays matching the sweep's query and would be retried every tick
        /// forever. The identity still pins it, so a rescan with new files probes again.
        /// </summary>
        [Fact]
        public async Task Probe_UndecodableFile_StoresUnknownSoTheSweepConverges()
        {
            GivenBook(("a.mp3", 3600));
            _reader.Setup(r => r.ReadWindowsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AudioSampleWindows?)null);

            AudioQualityRecord? stored = null;
            _audiobooks.Setup(r => r.SetAudioQualityAsync(7, It.IsAny<AudioQualityRecord>(), It.IsAny<CancellationToken>()))
                .Callback<int, AudioQualityRecord, CancellationToken>((_, record, _) => stored = record)
                .Returns(Task.CompletedTask);

            var report = await BuildService().ProbeAsync(7);

            Assert.Equal(AudioQuality.Unknown, report.Verdict);
            Assert.NotNull(stored);
            Assert.Equal(AudioQualityIdentity.Of([3600]), stored!.FileIdentity);
        }

        /// <summary>A missing decoder is the environment's fault; nothing is recorded against the book.</summary>
        [Fact]
        public async Task Probe_NoDecoder_ThrowsWithoutStoring()
        {
            GivenBook(("a.mp3", 3600));
            _reader.Setup(r => r.ReadWindowsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("No ffmpeg is installed."));

            await Assert.ThrowsAsync<InvalidOperationException>(() => BuildService().ProbeAsync(7));

            _audiobooks.Verify(
                r => r.SetAudioQualityAsync(It.IsAny<int>(), It.IsAny<AudioQualityRecord>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Enqueue_AutomaticWhileDisabled_Refuses()
        {
            var settings = new ApplicationSettingsBuilder().Build();
            settings.AudioQualityProbeEnabled = false;
            _configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Automatic);

            Assert.Equal(TagEnqueueOutcome.Disabled, result.Outcome);
            _queue.Verify(
                q => q.EnqueueAudioQualityProbeAsync(It.IsAny<int>(), It.IsAny<TagTrigger>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>A person asking directly outranks the automatic switch, as with every other job.</summary>
        [Fact]
        public async Task Enqueue_ManualWhileDisabled_StillQueues()
        {
            var settings = new ApplicationSettingsBuilder().Build();
            settings.AudioQualityProbeEnabled = false;
            _configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(settings);
            _queue.Setup(q => q.EnqueueAudioQualityProbeAsync(7, TagTrigger.Manual, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TagEnqueueResult(TagEnqueueOutcome.Queued, Guid.NewGuid()));

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Manual);

            Assert.Equal(TagEnqueueOutcome.Queued, result.Outcome);
        }
    }
}
