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
using Listenarr.Application.Audiobooks.Conversion;
using Listenarr.Infrastructure.Ffmpeg.Conversion;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Ffmpeg.Conversion
{
    /// <summary>
    /// Exercises the real encoder. These run wherever ffmpeg and ffprobe are on PATH —
    /// the Docker dev environment installs both — and are skipped elsewhere, because a
    /// host without an encoder proves nothing either way.
    /// </summary>
    [Trait("Name", "FfmpegAudiobookConverterTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class FfmpegAudiobookConverterTests : BaseTests, IDisposable
    {
        private readonly string _workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "listenarr-conversion-" + Guid.NewGuid().ToString("N"));

        private static string? FindOnPath(string binary) =>
            EncoderFactAttribute.FindOnPath(binary);

        private FfmpegAudiobookConverter BuildConverter() =>
            new(
                new PathResolvedFfmpegService(FindOnPath("ffmpeg")!, FindOnPath("ffprobe")!),
                new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance),
                NullLogger<FfmpegAudiobookConverter>.Instance);

        /// <summary>Generate a real MP3 of the requested length and stream shape.</summary>
        private async Task<string> WriteSourceMp3Async(
            string name,
            int seconds,
            int sampleRate = 44_100,
            int channels = 2)
        {
            Directory.CreateDirectory(_workingDirectory);
            var path = Path.Combine(_workingDirectory, name);

            var startInfo = new ProcessStartInfo
            {
                FileName = FindOnPath("ffmpeg")!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
                     {
                         "-hide_banner", "-loglevel", "error", "-y",
                         "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
                         "-ar", sampleRate.ToString(), "-ac", channels.ToString(),
                         "-c:a", "libmp3lame", "-b:a", "64k",
                         path
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            var runner = new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance);
            var result = await runner.RunAsync(startInfo, 30_000);
            Assert.Equal(0, result.ExitCode);
            return path;
        }

        private ConversionSource Source(string path, int seconds, int sampleRate = 44_100, int channels = 2) =>
            new(path, Path.GetFileName(path), TimeSpan.FromSeconds(seconds), 64_000, sampleRate, channels);

        private string OutputPath => Path.Combine(_workingDirectory, "out.m4b");

        /// <summary>Rewrite a source MP3 with ID3 chapter marks, the way a merge tool would.</summary>
        private async Task<string> WriteChapteredCopyAsync(
            string sourcePath,
            IReadOnlyList<(string Title, int Start, int End)> chapters)
        {
            var metadataPath = Path.Combine(_workingDirectory, "chapters.ffmetadata");
            var builder = new System.Text.StringBuilder(";FFMETADATA1\n");
            foreach (var chapter in chapters)
            {
                builder.Append("[CHAPTER]\nTIMEBASE=1/1000\n");
                builder.Append($"START={chapter.Start * 1000}\nEND={chapter.End * 1000}\n");
                builder.Append($"title={chapter.Title}\n");
            }

            await File.WriteAllTextAsync(metadataPath, builder.ToString());

            var target = Path.Combine(_workingDirectory, "chaptered.mp3");
            var startInfo = new ProcessStartInfo
            {
                FileName = FindOnPath("ffmpeg")!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
                     {
                         "-hide_banner", "-loglevel", "error", "-y",
                         "-i", sourcePath, "-i", metadataPath,
                         "-map", "0:a", "-map_metadata", "1", "-c:a", "copy",
                         target
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            var runner = new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance);
            var result = await runner.RunAsync(startInfo, 30_000);
            Assert.Equal(0, result.ExitCode);
            return target;
        }

        // ---- the happy path ---------------------------------------------------------

        [EncoderFact]
        public async Task ConvertAsync_ProducesOneChapterPerSourceFileInOrder()
        {
            var first = await WriteSourceMp3Async("Chapter 1.mp3", 2);
            var second = await WriteSourceMp3Async("Chapter 2.mp3", 3);
            var tenth = await WriteSourceMp3Async("Chapter 10.mp3", 1);

            var plan = ConversionPlanner.BuildPlan(
                [Source(tenth, 1), Source(first, 2), Source(second, 3)],
                StringComparer.Ordinal);

            var result = await BuildConverter().ConvertAsync(
                new ConversionRequest(plan, OutputPath, new AudioMetadata { Title = "Test Book" }));

            Assert.True(result.Success, result.Message);
            Assert.Equal(3, result.ChapterCount);

            // Natural order, not lexical: the one-second "Chapter 10" file is last.
            var chapters = await ReadChaptersAsync(OutputPath);
            Assert.Equal(["Chapter 1", "Chapter 2", "Chapter 10"], chapters.Select(c => c.Title));
            Assert.Equal(0, chapters[0].Start, 1);
            Assert.Equal(2, chapters[1].Start, 1);
            Assert.Equal(5, chapters[2].Start, 1);
        }

        [EncoderFact]
        public async Task ConvertAsync_WritesTheDescriptionIntoTheDescAtom()
        {
            var source = await WriteSourceMp3Async("Chapter 1.mp3", 1);
            var plan = ConversionPlanner.BuildPlan([Source(source, 1)], StringComparer.Ordinal);

            var result = await BuildConverter().ConvertAsync(
                new ConversionRequest(
                    plan,
                    OutputPath,
                    new AudioMetadata { Title = "Test Book", Description = "A hobbit leaves home." }));

            Assert.True(result.Success, result.Message);

            // The atom itself, not ffprobe's normalised view of it: Plex reads an album
            // summary from the MP4 desc atom and from nothing else. ffmpeg could satisfy
            // ffprobe by writing the value somewhere else entirely.
            var bytes = await File.ReadAllBytesAsync(OutputPath);
            var descAtom = IndexOf(bytes, "desc"u8);
            Assert.True(descAtom >= 0, "The output carries no desc atom.");

            // The value has to sit inside that atom's payload, not merely somewhere in
            // the file: an ID3-style tag elsewhere would not reach Plex.
            var value = IndexOf(bytes, "A hobbit leaves home."u8);
            Assert.True(value > descAtom, "The description is not stored in the desc atom.");
            Assert.True(value - descAtom < 64, "The description is too far from the desc atom header to be its payload.");
        }

        [EncoderFact]
        public async Task ConvertAsync_KeepsTheStrongestStreamParameters_WhenSourcesDiffer()
        {
            // The concat demuxer would adopt the first file's 22kHz mono and silently
            // downmix the second. The output must keep 44.1kHz stereo.
            var quiet = await WriteSourceMp3Async("01.mp3", 2, sampleRate: 22_050, channels: 1);
            var loud = await WriteSourceMp3Async("02.mp3", 2, sampleRate: 44_100, channels: 2);

            var plan = ConversionPlanner.BuildPlan(
                [Source(quiet, 2, 22_050, 1), Source(loud, 2, 44_100, 2)],
                StringComparer.Ordinal);

            var result = await BuildConverter().ConvertAsync(
                new ConversionRequest(plan, OutputPath, new AudioMetadata()));

            Assert.True(result.Success, result.Message);

            var (sampleRate, channels) = await ReadStreamShapeAsync(OutputPath);
            Assert.Equal(44_100, sampleRate);
            Assert.Equal(2, channels);
        }

        [EncoderFact]
        public async Task ConvertAsync_ReportsProgressThatReachesCompletion()
        {
            var source = await WriteSourceMp3Async("Chapter 1.mp3", 4);
            var plan = ConversionPlanner.BuildPlan([Source(source, 4)], StringComparer.Ordinal);

            var reported = new List<double>();
            var result = await BuildConverter().ConvertAsync(
                new ConversionRequest(plan, OutputPath, new AudioMetadata()),
                new Progress<ConversionProgress>(p => reported.Add(p.Fraction)));

            Assert.True(result.Success, result.Message);
            Assert.NotEmpty(reported);
            Assert.All(reported, f => Assert.InRange(f, 0, 1));
        }

        [EncoderFact]
        public async Task ConvertAsync_KeepsTheChaptersOfAnAlreadyMergedFile()
        {
            // The library may already hold books merged into one chaptered MP3. Those
            // marks have to survive the conversion, or converting them loses more than
            // it gains.
            var source = await WriteSourceMp3Async("Whole Book.mp3", 9);
            var chaptered = await WriteChapteredCopyAsync(
                source,
                [("Opening", 0, 3), ("Middle", 3, 6), ("End", 6, 9)]);

            var plan = ConversionPlanner.BuildPlan(
                [
                    Source(chaptered, 9) with
                    {
                        EmbeddedChapters =
                        [
                            new EmbeddedChapter("Opening", TimeSpan.Zero, TimeSpan.FromSeconds(3)),
                            new EmbeddedChapter("Middle", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6)),
                            new EmbeddedChapter("End", TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(9)),
                        ]
                    }
                ],
                StringComparer.Ordinal);

            var result = await BuildConverter().ConvertAsync(
                new ConversionRequest(plan, OutputPath, new AudioMetadata { Title = "Merged Book" }));

            Assert.True(result.Success, result.Message);
            Assert.Equal(3, result.ChapterCount);

            var chapters = await ReadChaptersAsync(OutputPath);
            Assert.Equal(["Opening", "Middle", "End"], chapters.Select(c => c.Title));
            Assert.Equal(3, chapters[1].Start, 1);
            Assert.Equal(6, chapters[2].Start, 1);
        }

        // ---- failure paths ----------------------------------------------------------

        [EncoderFact]
        public async Task ConvertAsync_ReportsTheMissingFile_WhenASourceHasGone()
        {
            var present = await WriteSourceMp3Async("Chapter 1.mp3", 1);
            var missing = Path.Combine(_workingDirectory, "Chapter 2.mp3");

            var plan = ConversionPlanner.BuildPlan(
                [Source(present, 1), Source(missing, 1)],
                StringComparer.Ordinal);

            var result = await BuildConverter().ConvertAsync(
                new ConversionRequest(plan, OutputPath, new AudioMetadata()));

            Assert.False(result.Success);
            Assert.Equal(ConversionFailureKind.SourceUnreadable, result.FailureKind);
            // The reason has to name the file; "conversion failed" is not actionable.
            Assert.Contains("Chapter 2.mp3", result.Message);
            Assert.False(File.Exists(OutputPath));
        }

        [EncoderFact]
        public async Task ConvertAsync_FailsWithoutWritingOutput_WhenASourceIsNotAudio()
        {
            Directory.CreateDirectory(_workingDirectory);
            var corrupt = Path.Combine(_workingDirectory, "Chapter 1.mp3");
            await File.WriteAllTextAsync(corrupt, "this is not an audio file");

            var plan = ConversionPlanner.BuildPlan([Source(corrupt, 1)], StringComparer.Ordinal);

            var result = await BuildConverter().ConvertAsync(
                new ConversionRequest(plan, OutputPath, new AudioMetadata()));

            Assert.False(result.Success);
            Assert.Equal(ConversionFailureKind.EncodeFailed, result.FailureKind);
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
        }

        [Fact]
        public async Task ConvertAsync_RefusesToStart_WhenNoEncoderIsInstalled()
        {
            var converter = new FfmpegAudiobookConverter(
                new PathResolvedFfmpegService(ffmpegPath: null, ffprobePath: null),
                new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance),
                NullLogger<FfmpegAudiobookConverter>.Instance);

            var plan = ConversionPlanner.BuildPlan(
                [new ConversionSource("/nowhere/Chapter 1.mp3", null, TimeSpan.FromSeconds(1), 64_000, 44_100, 2)],
                StringComparer.Ordinal);

            var result = await converter.ConvertAsync(
                new ConversionRequest(plan, OutputPath, new AudioMetadata()));

            Assert.False(result.Success);
            Assert.Equal(ConversionFailureKind.EncoderUnavailable, result.FailureKind);
            Assert.False(await converter.IsAvailableAsync());
        }

        // ---- helpers ----------------------------------------------------------------

        /// <summary>Byte offset of the first occurrence of <paramref name="needle"/>, or -1.</summary>
        private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) =>
            haystack.IndexOf(needle);

        private static async Task<List<(string Title, double Start)>> ReadChaptersAsync(string path)
        {
            var json = await ProbeAsync(path, "-show_chapters");
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.GetProperty("chapters").EnumerateArray()
                .Select(c => (
                    c.GetProperty("tags").GetProperty("title").GetString() ?? string.Empty,
                    double.Parse(c.GetProperty("start_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();
        }

        private static async Task<(int SampleRate, int Channels)> ReadStreamShapeAsync(string path)
        {
            var json = await ProbeAsync(path, "-show_streams");
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var audio = document.RootElement.GetProperty("streams").EnumerateArray()
                .First(s => s.GetProperty("codec_type").GetString() == "audio");
            return (
                int.Parse(audio.GetProperty("sample_rate").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                audio.GetProperty("channels").GetInt32());
        }

        private static async Task<string> ProbeAsync(string path, string what)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = FindOnPath("ffprobe")!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-v", "error", "-print_format", "json", what, path })
            {
                startInfo.ArgumentList.Add(argument);
            }

            var runner = new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance);
            var result = await runner.RunAsync(startInfo, 30_000);
            Assert.Equal(0, result.ExitCode);
            return result.Stdout;
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover scratch directory is not a test failure.
            }
        }

        /// <summary>
        /// An <see cref="IFfmpegService"/> that reports fixed binary paths, so the
        /// converter can be driven without the installer or its download.
        /// </summary>
        private sealed class PathResolvedFfmpegService(string? ffmpegPath, string? ffprobePath) : IFfmpegService
        {
            public Task<string?> GetFfmpegPathAsync() => Task.FromResult(ffmpegPath);
            public Task<string?> EnsureFfmpegInstalledAsync() => Task.FromResult(ffmpegPath);
            public Task<string?> GetFfprobePathAsync() => Task.FromResult(ffprobePath);
            public Task<string?> EnsureFfprobeInstalledAsync() => Task.FromResult(ffprobePath);
            public Task<string> GetLicenseAsync() => Task.FromResult(string.Empty);

            public Task<IReadOnlyList<EmbeddedChapter>> ReadChaptersAsync(
                string filePath,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<EmbeddedChapter>>([]);

            public Task<AudioMetadata> RunFfprobeAsync(string filePath) =>
                throw new NotSupportedException();

            public Task<AudioMetadata> RunFfprobeAsync(MetadataFileSource fileSource) =>
                throw new NotSupportedException();
        }
    }
}
