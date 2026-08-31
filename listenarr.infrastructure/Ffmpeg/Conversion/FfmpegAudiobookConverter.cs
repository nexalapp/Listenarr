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
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Ffmpeg.Conversion
{
    /// <summary>
    /// Runs one ffmpeg encode per conversion and verifies what it produced.
    ///
    /// The output is written to the scratch path the caller names and is never moved
    /// into place here, so a failed or half-written encode cannot replace a file the
    /// library is serving.
    /// </summary>
    public sealed class FfmpegAudiobookConverter(
        IFfmpegService ffmpegService,
        IProcessRunner processRunner,
        ILogger<FfmpegAudiobookConverter> logger) : IAudiobookConverter
    {
        /// <summary>
        /// How far the output duration may sit from the sum of the source durations
        /// before the result is rejected. AAC frames are 1024 samples, and priming plus
        /// the final partial frame accounts for a fraction of a second; anything beyond
        /// this means inputs were dropped rather than merely padded.
        /// </summary>
        private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(2);

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            var path = await ffmpegService.GetFfmpegPathAsync();
            return !string.IsNullOrEmpty(path);
        }

        public Task<ConversionResult> VerifyExistingOutputAsync(
            ConversionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return VerifyOutputAsync(request, cancellationToken);
        }

        public async Task<ConversionResult> ConvertAsync(
            ConversionRequest request,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var ffmpegPath = await ffmpegService.GetFfmpegPathAsync();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                return ConversionResult.Fail(
                    ConversionFailureKind.EncoderUnavailable,
                    "No ffmpeg encoder is installed. Set LISTENARR_FFMPEG_PATH or let the bundled installer finish, then retry.");
            }

            // Check the sources before spending an encode on them: a missing file is the
            // common case after a share drops, and it reads far better as its own reason
            // than as ffmpeg's exit code.
            foreach (var source in request.Plan.OrderedSources)
            {
                if (!File.Exists(source.FullPath))
                {
                    return ConversionResult.Fail(
                        ConversionFailureKind.SourceUnreadable,
                        $"Source file is missing: {LogRedaction.SanitizeFilePath(source.FullPath)}");
                }
            }

            string? metadataPath = null;
            try
            {
                metadataPath = Path.Combine(
                    Path.GetDirectoryName(request.ScratchOutputPath) ?? Path.GetTempPath(),
                    Path.GetFileNameWithoutExtension(request.ScratchOutputPath) + ".ffmetadata");

                await File.WriteAllTextAsync(
                    metadataPath,
                    FfmpegCommandBuilder.BuildMetadataDocument(request.Plan, request.Tags),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);

                var arguments = FfmpegCommandBuilder.BuildArguments(
                    request.Plan,
                    metadataPath,
                    request.ScratchOutputPath,
                    request.CoverArtPath);

                var run = await RunEncoderAsync(
                    ffmpegPath,
                    arguments,
                    request.Plan.TotalDuration,
                    progress,
                    cancellationToken);

                if (run.ExitCode != 0)
                {
                    return ConversionResult.Fail(
                        ConversionFailureKind.EncodeFailed,
                        $"ffmpeg exited with code {run.ExitCode}: {FfmpegService.SummariseFfprobeFailure(run.Stderr)}");
                }

                return await VerifyOutputAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Conversion could not write its working files");
                return ConversionResult.Fail(
                    ConversionFailureKind.Transient,
                    $"Could not write the conversion's working files: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Unexpected failure converting to M4B");
                return ConversionResult.Fail(ConversionFailureKind.Unknown, ex.Message);
            }
            finally
            {
                if (metadataPath != null)
                {
                    try
                    {
                        if (File.Exists(metadataPath))
                        {
                            File.Delete(metadataPath);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogDebug(ex, "Could not remove the conversion metadata scratch file");
                    }
                }
            }
        }

        private async Task<(int ExitCode, string Stderr)> RunEncoderAsync(
            string ffmpegPath,
            IReadOnlyList<string> arguments,
            TimeSpan total,
            IProgress<ConversionProgress>? progress,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // ArgumentList, never a joined string: source paths come from the library and
            // may hold quotes or spaces, and the list form is passed to exec verbatim.
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = processRunner.StartProcess(startInfo);

            var stderr = new StringBuilder();
            var stderrPump = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync(cancellationToken)) != null)
                {
                    // Bounded: a source that warns on every frame would otherwise grow
                    // this without limit over a multi-hour encode.
                    if (stderr.Length < 16_384)
                    {
                        stderr.Append(line).Append('\n');
                    }
                }
            }, CancellationToken.None);

            var stdoutPump = Task.Run(
                () => PumpProgressAsync(process, total, progress, cancellationToken),
                CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            await Task.WhenAll(stderrPump, stdoutPump);
            return (process.ExitCode, stderr.ToString());
        }

        /// <summary>
        /// Read ffmpeg's <c>-progress</c> stream. It emits <c>key=value</c> lines and closes
        /// each block with <c>progress=continue</c> or <c>progress=end</c>; the useful field
        /// is <c>out_time_us</c>, the position reached in the output.
        /// </summary>
        private static async Task PumpProgressAsync(
            Process process,
            TimeSpan total,
            IProgress<ConversionProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (progress == null)
            {
                // Still drain it: a full pipe buffer would stall the encoder.
                await process.StandardOutput.ReadToEndAsync(cancellationToken);
                return;
            }

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                if (!line.AsSpan(0, separator).SequenceEqual("out_time_us"))
                {
                    continue;
                }

                if (!long.TryParse(
                        line.AsSpan(separator + 1),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var microseconds)
                    || microseconds < 0)
                {
                    continue;
                }

                var encoded = TimeSpan.FromMilliseconds(microseconds / 1000.0);
                var fraction = total > TimeSpan.Zero
                    ? Math.Clamp(encoded.TotalSeconds / total.TotalSeconds, 0, 1)
                    : 0;

                progress.Report(new ConversionProgress(fraction, encoded, total));
            }
        }

        /// <summary>
        /// Prove the encoder produced what was asked for. A zero exit code is not enough:
        /// the point of converting is the chapters, and a file that lost them is a
        /// failure even though ffmpeg was happy.
        /// </summary>
        private async Task<ConversionResult> VerifyOutputAsync(
            ConversionRequest request,
            CancellationToken cancellationToken)
        {
            var output = request.ScratchOutputPath;
            if (!File.Exists(output))
            {
                return ConversionResult.Fail(
                    ConversionFailureKind.OutputRejected,
                    "ffmpeg reported success but wrote no output file.");
            }

            if (new FileInfo(output).Length == 0)
            {
                return ConversionResult.Fail(
                    ConversionFailureKind.OutputRejected,
                    "ffmpeg reported success but the output file is empty.");
            }

            var ffprobePath = await ffmpegService.GetFfprobePathAsync();
            if (string.IsNullOrEmpty(ffprobePath))
            {
                // Without ffprobe the file cannot be checked. Say so rather than
                // reporting an unverified conversion as verified.
                return ConversionResult.Fail(
                    ConversionFailureKind.OutputRejected,
                    "The converted file could not be verified because ffprobe is unavailable.");
            }

            var probeInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            probeInfo.ArgumentList.Add("-v");
            probeInfo.ArgumentList.Add("error");
            probeInfo.ArgumentList.Add("-print_format");
            probeInfo.ArgumentList.Add("json");
            probeInfo.ArgumentList.Add("-show_format");
            probeInfo.ArgumentList.Add("-show_chapters");
            probeInfo.ArgumentList.Add(output);

            var probe = await processRunner.RunAsync(probeInfo, 60_000, cancellationToken);
            if (probe.TimedOut || probe.ExitCode != 0)
            {
                return ConversionResult.Fail(
                    ConversionFailureKind.OutputRejected,
                    $"The converted file could not be read back: {FfmpegService.SummariseFfprobeFailure(probe.Stderr)}");
            }

            TimeSpan duration;
            int chapterCount;
            try
            {
                using var document = JsonDocument.Parse(probe.Stdout);
                var root = document.RootElement;

                chapterCount = root.TryGetProperty("chapters", out var chapters)
                    ? chapters.GetArrayLength()
                    : 0;

                duration = root.TryGetProperty("format", out var format)
                           && format.TryGetProperty("duration", out var durationValue)
                           && double.TryParse(
                               durationValue.GetString(),
                               NumberStyles.Float,
                               CultureInfo.InvariantCulture,
                               out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : TimeSpan.Zero;
            }
            catch (JsonException ex)
            {
                return ConversionResult.Fail(
                    ConversionFailureKind.OutputRejected,
                    $"The converted file's metadata could not be parsed: {ex.Message}");
            }

            var expectedChapters = request.Plan.Chapters.Count;
            if (chapterCount != expectedChapters)
            {
                return ConversionResult.Fail(
                    ConversionFailureKind.OutputRejected,
                    $"The converted file has {chapterCount} chapter(s) but {expectedChapters} were expected. The original files have been left alone.");
            }

            var expectedDuration = request.Plan.TotalDuration;
            if (expectedDuration > TimeSpan.Zero)
            {
                var drift = (duration - expectedDuration).Duration();
                if (drift > DurationTolerance)
                {
                    return ConversionResult.Fail(
                        ConversionFailureKind.OutputRejected,
                        $"The converted file runs {duration:hh\\:mm\\:ss} but the source files total {expectedDuration:hh\\:mm\\:ss}. The original files have been left alone.");
                }
            }

            logger.LogInformation(
                "Conversion produced a {Duration} file with {Chapters} chapter(s)",
                duration,
                chapterCount);

            return ConversionResult.Ok(duration, chapterCount);
        }

        private void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
            {
                logger.LogDebug(ex, "Could not stop the ffmpeg process after cancellation");
            }
        }
    }
}
