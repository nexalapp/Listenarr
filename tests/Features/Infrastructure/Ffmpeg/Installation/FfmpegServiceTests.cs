using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Ffmpeg.Installation
{
    [Trait("Name", "FfmpegServiceTests")]
    [Trait("Category", "FfmpegService")]
    public class FfmpegServiceTests : BaseTests
    {
        // FIXME: This is too longo for unit tests
        //[Fact]
        [Trait("Method", "EnsureFfprobeInstalledAsync")]
        [Trait("Category", "Release")]
        private async Task EnsureFfprobeInstalledAsync()
        {
            var ffmpegDirectory = Path.Combine(FileService.GetTempPath(), "ffmpeg");

            Assert.False(Path.Exists(ffmpegDirectory));

            var ffmpegService = new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                new HttpClient(),
                _provider.GetRequiredService<IStartupConfigService>(),
                _provider.GetRequiredService<IProcessRunner>(),
                Mock.Of<IApplicationPathService>(service => service.FfmpegRootPath == ffmpegDirectory));

            var ffprobePath = await ffmpegService.EnsureFfprobeInstalledAsync();

            Assert.NotNull(ffprobePath);
            Assert.True(Path.Exists(ffprobePath));
            Assert.True(Path.Exists(ffmpegDirectory));
        }

        [Fact]
        public async Task RunFfprobeAsync_RejectsNonAudioFileBeforeStartingProcess()
        {
            var ffmpegDirectory = FileService.GetTempDirectory("ffprobe-root");
            var ffprobePath = Path.Join(ffmpegDirectory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            await File.WriteAllTextAsync(ffprobePath, "fake");
            var textFile = await FileService.GetFileAsync(FileService.GetTempDirectory("ffprobe-target"), "notes.txt", "not audio");

            var processRunner = new Mock<IProcessRunner>();
            var service = new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                new HttpClient(),
                _provider.GetRequiredService<IStartupConfigService>(),
                processRunner.Object,
                Mock.Of<IApplicationPathService>(applicationPathService => applicationPathService.FfmpegRootPath == ffmpegDirectory));

            await Assert.ThrowsAsync<FfmpegException>(() => service.RunFfprobeAsync(textFile));
            processRunner.Verify(runner => runner.RunAsync(It.IsAny<System.Diagnostics.ProcessStartInfo>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task RunFfprobeAsync_ExtensionlessStablePath_UsesPublicIdentityForValidationAndMapping()
        {
            var ffmpegDirectory = FileService.GetTempDirectory("ffprobe-root");
            var ffprobePath = Path.Join(
                ffmpegDirectory,
                OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            await File.WriteAllTextAsync(ffprobePath, "fake");
            var stableReadPath = await FileService.GetFileAsync(
                FileService.GetTempDirectory("ffprobe-stable-target"),
                "42",
                "audio");
            var publicPath = Path.Join("library", "Public.Name.M4B");
            System.Diagnostics.ProcessStartInfo? capturedStartInfo = null;
            var processRunner = new Mock<IProcessRunner>();
            processRunner.Setup(runner => runner.RunAsync(
                    It.IsAny<System.Diagnostics.ProcessStartInfo>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Callback<System.Diagnostics.ProcessStartInfo, int, CancellationToken>(
                    (startInfo, _, _) => capturedStartInfo = startInfo)
                .ReturnsAsync(new ProcessResult(
                    0,
                    "{\"format\":{\"format_name\":\"mov\",\"duration\":\"1\"},\"streams\":[]}",
                    string.Empty,
                    false));
            var service = new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                new HttpClient(),
                _provider.GetRequiredService<IStartupConfigService>(),
                processRunner.Object,
                Mock.Of<IApplicationPathService>(applicationPathService =>
                    applicationPathService.FfmpegRootPath == ffmpegDirectory));

            var metadata = await service.RunFfprobeAsync(
                new MetadataFileSource(stableReadPath, publicPath));

            Assert.NotNull(capturedStartInfo);
            Assert.Equal(
                Path.GetFullPath(stableReadPath),
                capturedStartInfo.ArgumentList[^1]);
            Assert.Equal("Public.Name", metadata.Title);
            Assert.Equal("M4B", metadata.Format);
            Assert.Equal("M4B", metadata.Container);
        }

        [LinuxFact]
        public async Task RunFfprobeAsync_LinuxPinnedDescriptor_ReadsStableBytesAndMapsPublicIdentity()
        {
            var source = await FileService.GetFileAsync(
                FileService.GetTempDirectory("ffprobe-linux-source"),
                "Source.m4b",
                "audio");
            var destination = Path.Join(
                FileService.GetTempDirectory("ffprobe-linux-destination"),
                "Public.Name.M4B");
            var movedDestination = Path.Join(
                Path.GetDirectoryName(destination)!,
                "renamed-after-lease.bin");
            var mover = _provider.GetRequiredService<FileMover>();
            using var lease = await mover.PrepareActionForRegistrationAsync(
                FileAction.Copy,
                source,
                destination,
                Guid.NewGuid());
            Assert.NotNull(lease);
            File.Move(destination, movedDestination);

            var ffmpegDirectory = FileService.GetTempDirectory("ffprobe-root");
            var ffprobePath = Path.Join(ffmpegDirectory, "ffprobe");
            await File.WriteAllTextAsync(ffprobePath, "fake");
            System.Diagnostics.ProcessStartInfo? capturedStartInfo = null;
            var processRunner = new Mock<IProcessRunner>();
            processRunner.Setup(runner => runner.RunAsync(
                    It.IsAny<System.Diagnostics.ProcessStartInfo>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Callback<System.Diagnostics.ProcessStartInfo, int, CancellationToken>(
                    (startInfo, _, _) => capturedStartInfo = startInfo)
                .ReturnsAsync(new ProcessResult(
                    0,
                    "{\"format\":{\"format_name\":\"mov\",\"duration\":\"1\"},\"streams\":[]}",
                    string.Empty,
                    false));
            var service = new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                new HttpClient(),
                _provider.GetRequiredService<IStartupConfigService>(),
                processRunner.Object,
                Mock.Of<IApplicationPathService>(applicationPathService =>
                    applicationPathService.FfmpegRootPath == ffmpegDirectory));

            var metadata = await service.RunFfprobeAsync(
                new MetadataFileSource(lease.MetadataPath, lease.PublicPath));

            Assert.NotNull(capturedStartInfo);
            Assert.Equal(
                Path.GetFullPath(lease.MetadataPath),
                capturedStartInfo.ArgumentList[^1]);
            Assert.Equal("Public.Name", metadata.Title);
            Assert.Equal("M4B", metadata.Format);
        }

        [Theory]
        [InlineData(1, false, "{\"format\":{}}")]
        [InlineData(-1, true, "{\"format\":{}}")]
        [InlineData(0, false, "")]
        [InlineData(0, false, "not-json")]
        public async Task RunFfprobeAsync_AnalyzerFailure_RejectsResult(
            int exitCode,
            bool timedOut,
            string stdout)
        {
            var ffmpegDirectory = FileService.GetTempDirectory("ffprobe-root");
            var ffprobePath = Path.Join(
                ffmpegDirectory,
                OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            await File.WriteAllTextAsync(ffprobePath, "fake");
            var stableReadPath = await FileService.GetFileAsync(
                FileService.GetTempDirectory("ffprobe-failure-target"),
                "42",
                "audio");
            var processRunner = new Mock<IProcessRunner>();
            processRunner.Setup(runner => runner.RunAsync(
                    It.IsAny<System.Diagnostics.ProcessStartInfo>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult(
                    exitCode,
                    stdout,
                    string.Empty,
                    timedOut));
            var service = new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                new HttpClient(),
                _provider.GetRequiredService<IStartupConfigService>(),
                processRunner.Object,
                Mock.Of<IApplicationPathService>(applicationPathService =>
                    applicationPathService.FfmpegRootPath == ffmpegDirectory));

            await Assert.ThrowsAsync<FfmpegException>(() =>
                service.RunFfprobeAsync(new MetadataFileSource(
                    stableReadPath,
                    Path.Join("library", "Public.Name.M4B"))));
        }

        [Fact]
        public async Task RunFfprobeAsync_RejectsMissingFileBeforeStartingProcess()
        {
            var ffmpegDirectory = FileService.GetTempDirectory("ffprobe-root");
            var ffprobePath = Path.Join(ffmpegDirectory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            await File.WriteAllTextAsync(ffprobePath, "fake");
            var missingFile = Path.Join(FileService.GetTempDirectory("ffprobe-target"), "missing.mp3");

            var processRunner = new Mock<IProcessRunner>();
            var service = new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                new HttpClient(),
                _provider.GetRequiredService<IStartupConfigService>(),
                processRunner.Object,
                Mock.Of<IApplicationPathService>(applicationPathService => applicationPathService.FfmpegRootPath == ffmpegDirectory));

            await Assert.ThrowsAsync<FfmpegException>(() => service.RunFfprobeAsync(missingFile));
            processRunner.Verify(runner => runner.RunAsync(It.IsAny<System.Diagnostics.ProcessStartInfo>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        [Trait("Method", "EnsureFfmpegInstalledAsync")]
        public async Task EnsureFfmpegInstalledAsync_FetchesTheArchiveWhenOnlyFfprobeIsInstalled()
        {
            // An ffprobe-only release left this exact directory behind: the probe in place,
            // no encoder beside it. Treating that ffprobe as proof the install was complete
            // skipped the download, so conversion stayed unavailable for good.
            var ffmpegDirectory = FileService.GetTempDirectory("ffmpeg-probe-only");
            var ffprobePath = Path.Join(
                ffmpegDirectory,
                OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            await File.WriteAllTextAsync(ffprobePath, "fake");
            Assert.False(File.Exists(Path.Join(
                ffmpegDirectory,
                OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg")));

            var handler = new RecordingHandler();
            var service = new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                new HttpClient(handler),
                _provider.GetRequiredService<IStartupConfigService>(),
                _provider.GetRequiredService<IProcessRunner>(),
                Mock.Of<IApplicationPathService>(
                    applicationPathService => applicationPathService.FfmpegRootPath == ffmpegDirectory));

            var resolved = await service.EnsureFfmpegInstalledAsync();

            // The download is stubbed out, so no encoder appears - what matters is that the
            // install was attempted at all rather than short-circuited by the existing probe.
            Assert.Equal(1, handler.Requests);
            Assert.Null(resolved);
        }

        [Fact]
        [Trait("Method", "EnsureFfprobeInstalledAsync")]
        public async Task EnsureFfprobeInstalledAsync_StillSkipsTheDownloadWhenTheProbeIsPresent()
        {
            // The encoder's need to re-fetch must not turn every startup into a download.
            var ffmpegDirectory = FileService.GetTempDirectory("ffmpeg-probe-present");
            var ffprobePath = Path.Join(
                ffmpegDirectory,
                OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            await File.WriteAllTextAsync(ffprobePath, "fake");

            var handler = new RecordingHandler();
            var service = new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                new HttpClient(handler),
                _provider.GetRequiredService<IStartupConfigService>(),
                _provider.GetRequiredService<IProcessRunner>(),
                Mock.Of<IApplicationPathService>(
                    applicationPathService => applicationPathService.FfmpegRootPath == ffmpegDirectory));

            var resolved = await service.EnsureFfprobeInstalledAsync();

            Assert.Equal(0, handler.Requests);
            Assert.Equal(ffprobePath, resolved);
        }

        /// <summary>
        /// Counts download attempts and refuses them, so the install flow is exercised up to
        /// the point of the fetch without reaching the network.
        /// </summary>
        private sealed class RecordingHandler : HttpMessageHandler
        {
            public int Requests { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests++;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            }
        }

        [EncoderFact]
        [Trait("Method", "ReadChaptersAsync")]
        public async Task ReadChaptersAsync_ReadsIdThreeChaptersFromAMergedMp3()
        {
            // A library may already hold books merged into one chaptered MP3. Those marks
            // live in ID3 CHAP frames, and a conversion that could not read them would
            // flatten the whole book into a single chapter.
            var ffmpegDirectory = FileService.GetTempDirectory("ffprobe-chapters");
            File.Copy(
                EncoderFactAttribute.FindOnPath("ffprobe")!,
                Path.Combine(ffmpegDirectory, "ffprobe"));

            var chaptered = await WriteChapteredMp3Async(
                FileService.GetTempDirectory("chaptered-source"),
                [("Opening", 0, 3), ("Middle", 3, 6), ("End", 6, 9)]);

            var service = new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                new HttpClient(),
                _provider.GetRequiredService<IStartupConfigService>(),
                _provider.GetRequiredService<IProcessRunner>(),
                Mock.Of<IApplicationPathService>(paths => paths.FfmpegRootPath == ffmpegDirectory));

            var chapters = await service.ReadChaptersAsync(chaptered);

            Assert.Equal(3, chapters.Count);
            Assert.Equal(["Opening", "Middle", "End"], chapters.Select(chapter => chapter.Title));
            Assert.Equal(TimeSpan.FromSeconds(3), chapters[1].Start);
            Assert.Equal(TimeSpan.FromSeconds(9), chapters[2].End);
        }

        [EncoderFact]
        [Trait("Method", "ReadChaptersAsync")]
        public async Task ReadChaptersAsync_ReturnsNothingForAFileWithNoChapters()
        {
            var ffmpegDirectory = FileService.GetTempDirectory("ffprobe-nochapters");
            File.Copy(
                EncoderFactAttribute.FindOnPath("ffprobe")!,
                Path.Combine(ffmpegDirectory, "ffprobe"));

            var plain = await WritePlainMp3Async(FileService.GetTempDirectory("plain-source"));

            var service = new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                new HttpClient(),
                _provider.GetRequiredService<IStartupConfigService>(),
                _provider.GetRequiredService<IProcessRunner>(),
                Mock.Of<IApplicationPathService>(paths => paths.FfmpegRootPath == ffmpegDirectory));

            Assert.Empty(await service.ReadChaptersAsync(plain));
        }

        private static async Task<string> WritePlainMp3Async(string directory)
        {
            var path = Path.Combine(directory, "plain.mp3");
            await RunFfmpegAsync(
                "-f", "lavfi", "-i", "sine=frequency=440:duration=9",
                "-c:a", "libmp3lame", "-b:a", "64k", path);
            return path;
        }

        /// <summary>Write an MP3 carrying ID3 chapter marks, the way a merge tool would.</summary>
        private static async Task<string> WriteChapteredMp3Async(
            string directory,
            IReadOnlyList<(string Title, int Start, int End)> chapters)
        {
            var source = await WritePlainMp3Async(directory);

            var metadata = new System.Text.StringBuilder(";FFMETADATA1\n");
            foreach (var chapter in chapters)
            {
                metadata.Append("[CHAPTER]\nTIMEBASE=1/1000\n");
                metadata.Append($"START={chapter.Start * 1000}\nEND={chapter.End * 1000}\n");
                metadata.Append($"title={chapter.Title}\n");
            }

            var metadataPath = Path.Combine(directory, "chapters.ffmetadata");
            await File.WriteAllTextAsync(metadataPath, metadata.ToString());

            var target = Path.Combine(directory, "chaptered.mp3");
            await RunFfmpegAsync(
                "-i", source, "-i", metadataPath,
                "-map", "0:a", "-map_metadata", "1", "-c:a", "copy", target);
            return target;
        }

        private static async Task RunFfmpegAsync(params string[] arguments)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = EncoderFactAttribute.FindOnPath("ffmpeg")!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }

            var runner = new Listenarr.Infrastructure.SystemDiagnostics.Processes.SystemProcessRunner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Listenarr.Infrastructure.SystemDiagnostics.Processes.SystemProcessRunner>.Instance);
            var result = await runner.RunAsync(startInfo, 30_000);
            Assert.Equal(0, result.ExitCode);
        }
    }
}
