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
using System.Diagnostics;
using Listenarr.Infrastructure.Ffmpeg.Analysis;
using Listenarr.Application.Audiobooks.Chapters;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Ffmpeg.Analysis
{
    [Trait("Name", "FfmpegSilenceDetectorTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class FfmpegSilenceDetectorTests : BaseTests, IDisposable
    {
        private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "listenarr-silence-" + Guid.NewGuid().ToString("N"));

        [Theory]
        [InlineData("[silencedetect @ 0x55d1] silence_end: 125.9 | silence_duration: 2.44", 123.46, 125.9)]
        [InlineData("silence_end: 7 | silence_duration: 1", 6, 7)]
        public void Parse_ReadsThePauseALineCloses(string line, double start, double end)
        {
            var silence = FfmpegSilenceDetector.Parse(line);

            Assert.NotNull(silence);
            Assert.Equal(start, silence!.Value.Start.TotalSeconds, 2);
            Assert.Equal(end, silence.Value.End.TotalSeconds, 2);
        }

        [Theory]
        [InlineData("[silencedetect @ 0x55d1] silence_start: 123.46")]
        [InlineData("size=N/A time=00:12:00.00 bitrate=N/A speed= 210x")]
        [InlineData("silence_end: 12 | silence_duration: 0")]
        [InlineData("")]
        public void Parse_IgnoresEveryOtherLine(string line)
        {
            Assert.Null(FfmpegSilenceDetector.Parse(line));
        }

        [EncoderFact]
        public async Task DetectAsync_FindsThePausesInARealFile()
        {
            // Four seconds of tone, three of silence, four of tone, one of silence, four of tone.
            var path = await WriteAsync("pauses.mp3", "sine=frequency=440:duration=4[a];anullsrc=r=44100:cl=mono,atrim=duration=3[s1];sine=frequency=440:duration=4[b];anullsrc=r=44100:cl=mono,atrim=duration=1[s2];sine=frequency=440:duration=4[c];[a][s1][b][s2][c]concat=n=5:v=0:a=1");
            var detector = BuildDetector();

            var pauses = await detector.DetectAsync(path, TimeSpan.FromSeconds(0.8));

            Assert.Equal(2, pauses.Count);
            Assert.Equal(4, pauses[0].Start.TotalSeconds, 0.3);
            Assert.Equal(7, pauses[0].End.TotalSeconds, 0.3);
            Assert.Equal(11, pauses[1].Start.TotalSeconds, 0.3);
            Assert.Equal(12, pauses[1].End.TotalSeconds, 0.3);
        }

        [EncoderFact]
        public async Task DetectAsync_LeavesOutPausesShorterThanAsked()
        {
            var path = await WriteAsync("short.mp3", "sine=frequency=440:duration=4[a];anullsrc=r=44100:cl=mono,atrim=duration=3[s1];sine=frequency=440:duration=4[b];anullsrc=r=44100:cl=mono,atrim=duration=1[s2];sine=frequency=440:duration=4[c];[a][s1][b][s2][c]concat=n=5:v=0:a=1");

            var pauses = await BuildDetector().DetectAsync(path, TimeSpan.FromSeconds(2));

            var pause = Assert.Single(pauses);
            Assert.Equal(7, pause.End.TotalSeconds, 0.3);
        }

        [EncoderFact]
        public async Task DetectAsync_ThrowsForAFileThatIsNotAudio()
        {
            Directory.CreateDirectory(_workingDirectory);
            var path = Path.Combine(_workingDirectory, "not-audio.m4b");
            await File.WriteAllTextAsync(path, "this is not an audio file");

            var ex = await Assert.ThrowsAsync<SilenceDetectionException>(() => BuildDetector().DetectAsync(path, TimeSpan.FromSeconds(1)));

            Assert.Contains("could not decode", ex.Message);
        }

        [Fact]
        public async Task DetectAsync_ThrowsWhenThereIsNoFfmpeg()
        {
            var detector = new FfmpegSilenceDetector(
                new FixedFfmpegService(null),
                new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance),
                NullLogger<FfmpegSilenceDetector>.Instance);

            await Assert.ThrowsAsync<SilenceDetectionException>(() => detector.DetectAsync("/nowhere.m4b", TimeSpan.FromSeconds(1)));
        }

        private static FfmpegSilenceDetector BuildDetector() =>
            new(
                new FixedFfmpegService(EncoderFactAttribute.FindOnPath("ffmpeg")),
                new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance),
                NullLogger<FfmpegSilenceDetector>.Instance);

        private async Task<string> WriteAsync(string name, string filterGraph)
        {
            Directory.CreateDirectory(_workingDirectory);
            var path = Path.Combine(_workingDirectory, name);
            var startInfo = new ProcessStartInfo
            {
                FileName = EncoderFactAttribute.FindOnPath("ffmpeg")!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", filterGraph, "-ac", "1", "-c:a", "libmp3lame", "-b:a", "64k", path })
            {
                startInfo.ArgumentList.Add(argument);
            }

            var result = await new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance).RunAsync(startInfo, 30_000);
            Assert.True(result.ExitCode == 0, result.Stderr);
            return path;
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
            catch (IOException)
            {
            }
        }

        private sealed class FixedFfmpegService(string? ffmpegPath) : IFfmpegService
        {
            public Task<string?> GetFfmpegPathAsync() => Task.FromResult(ffmpegPath);
            public Task<string?> EnsureFfmpegInstalledAsync() => Task.FromResult(ffmpegPath);
            public Task<string?> GetFfprobePathAsync() => Task.FromResult<string?>(null);
            public Task<string?> EnsureFfprobeInstalledAsync() => Task.FromResult<string?>(null);
            public Task<string> GetLicenseAsync() => Task.FromResult(string.Empty);
            public Task<IReadOnlyList<EmbeddedChapter>> ReadChaptersAsync(string filePath, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<EmbeddedChapter>>([]);
            public Task<TimeSpan?> MeasureDecodedDurationAsync(string filePath, CancellationToken cancellationToken = default) =>
                Task.FromResult<TimeSpan?>(null);
            public Task<AudioMetadata> RunFfprobeAsync(string filePath) => throw new NotSupportedException();
            public Task<AudioMetadata> RunFfprobeAsync(MetadataFileSource fileSource) => throw new NotSupportedException();
        }
    }
}
