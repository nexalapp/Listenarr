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

namespace Listenarr.Infrastructure.Ffmpeg.Tagging
{
    /// <summary>
    /// Reads and rewrites an MP4 container's metadata with ffmpeg.
    ///
    /// The rewrite is written to the scratch path the caller names and is never moved
    /// into place here, so a failed or half-written rewrite cannot replace a file the
    /// library is serving.
    /// </summary>
    public sealed class FfmpegTagWriter(
        IFfmpegService ffmpegService,
        IProcessRunner processRunner,
        ILogger<FfmpegTagWriter> logger) : IAudiobookTagWriter
    {
        /// <summary>
        /// How far the rewritten file's duration may sit from the original's.
        ///
        /// A stream copy changes no samples at all, so this is not a tolerance for
        /// encoding drift — it is only slack for the container rounding a duration to a
        /// different timebase. Anything larger means streams were dropped.
        /// </summary>
        private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Long enough for ffprobe to read a book-sized file across a NAS share that has
        /// gone to sleep, short enough that a hung probe does not hold a lease forever.
        /// </summary>
        private const int ProbeTimeoutMs = 120_000;

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            var ffmpeg = await ffmpegService.GetFfmpegPathAsync();
            if (string.IsNullOrEmpty(ffmpeg))
            {
                return false;
            }

            // Both binaries or neither: a rewrite that cannot be read back afterwards is
            // not something worth starting, because an unverified write is how this
            // silently does nothing.
            var ffprobe = await ffmpegService.GetFfprobePathAsync();
            return !string.IsNullOrEmpty(ffprobe);
        }

        public async Task<AudiobookFileTags> ReadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var probe = await ProbeAsync(filePath, cancellationToken);
            return probe.Tags
                ?? throw new FfmpegException(
                    $"Could not read the tags of {LogRedaction.SanitizeFilePath(filePath)}: {probe.Error}");
        }

        public async Task<TagWriteResult> WriteAsync(
            TagWriteRequest request,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var ffmpegPath = await ffmpegService.GetFfmpegPathAsync();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.WriterUnavailable,
                    "No ffmpeg is installed. Set LISTENARR_FFMPEG_PATH or let the bundled installer finish, then retry.");
            }

            if (!File.Exists(request.SourcePath))
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.SourceUnreadable,
                    $"The file is missing: {LogRedaction.SanitizeFilePath(request.SourcePath)}");
            }

            try
            {
                var arguments = FfmpegTagCommandBuilder.BuildArguments(
                    request.SourcePath,
                    request.ScratchOutputPath,
                    request.Tags,
                    request.Existing.HasCoverArt,
                    request.CoverArtPath);

                var run = await RunFfmpegAsync(
                    ffmpegPath,
                    arguments,
                    request.Existing.Duration,
                    progress,
                    cancellationToken);

                if (run.ExitCode != 0)
                {
                    return TagWriteResult.Fail(
                        TagWriteFailureKind.WriteFailed,
                        $"ffmpeg exited with code {run.ExitCode}: {FfmpegService.SummariseFfprobeFailure(run.Stderr)}");
                }

                return await VerifyAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "A tag write could not write its working file");
                return TagWriteResult.Fail(
                    TagWriteFailureKind.Transient,
                    $"Could not write the rewritten file: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Unexpected failure writing tags");
                return TagWriteResult.Fail(TagWriteFailureKind.Unknown, ex.Message);
            }
        }

        /// <summary>
        /// Prove the rewrite carries what was asked for.
        ///
        /// A zero exit code is not enough. ffmpeg drops chapters and cover art without
        /// complaint when they are not mapped, and a tag it could not represent is simply
        /// absent — all three look like success from the outside, and all three make the
        /// rewrite worse than not doing it. Every intended tag is read back and compared.
        /// </summary>
        private async Task<TagWriteResult> VerifyAsync(
            TagWriteRequest request,
            CancellationToken cancellationToken)
        {
            var output = request.ScratchOutputPath;
            if (!File.Exists(output))
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    "ffmpeg reported success but wrote no output file.");
            }

            if (new FileInfo(output).Length == 0)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    "ffmpeg reported success but the output file is empty.");
            }

            var probe = await ProbeAsync(output, cancellationToken);
            if (probe.Tags == null)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    $"The rewritten file could not be read back: {probe.Error} The original file has been left alone.");
            }

            var written = probe.Tags;

            if (written.ChapterCount != request.Existing.ChapterCount)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    $"The rewritten file has {written.ChapterCount} chapter(s) but the original has {request.Existing.ChapterCount}. The original file has been left alone.");
            }

            var expectedCover = request.Existing.HasCoverArt
                || !string.IsNullOrWhiteSpace(request.CoverArtPath);
            if (expectedCover && !written.HasCoverArt)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    "The rewritten file lost its cover art. The original file has been left alone.");
            }

            if (request.Existing.Duration > TimeSpan.Zero)
            {
                var drift = (written.Duration - request.Existing.Duration).Duration();
                if (drift > DurationTolerance)
                {
                    return TagWriteResult.Fail(
                        TagWriteFailureKind.OutputRejected,
                        $"The rewritten file runs {written.Duration:hh\\:mm\\:ss} but the original runs {request.Existing.Duration:hh\\:mm\\:ss}. The original file has been left alone.");
                }
            }

            var missing = new List<string>();
            foreach (var (key, expected) in request.Tags)
            {
                if (string.IsNullOrWhiteSpace(expected))
                {
                    continue;
                }

                if (!written.Tags.TryGetValue(key, out var actual)
                    || !TagValue.AreEquivalent(actual, expected))
                {
                    missing.Add(key);
                }
            }

            if (missing.Count > 0)
            {
                // Naming the tags matters: the usual cause is a key this container cannot
                // carry, and the operator's fix is to stop writing that one tag.
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    $"The rewritten file did not keep {missing.Count} tag(s) that were written ({string.Join(", ", missing.Take(6))}). The original file has been left alone.");
            }

            logger.LogInformation(
                "Tag rewrite verified: {Tags} tag(s), {Chapters} chapter(s), {Duration}",
                request.Tags.Count,
                written.ChapterCount,
                written.Duration);

            return TagWriteResult.Ok(request.Tags.Count);
        }

        private sealed record ProbeOutcome(AudiobookFileTags? Tags, string? Error);

        /// <summary>
        /// Read a file's format tags, chapter count, duration and whether it carries an
        /// attached picture — everything both the plan and the verification need, in one
        /// pass, because each pass is a full round trip to the share.
        /// </summary>
        private async Task<ProbeOutcome> ProbeAsync(string path, CancellationToken cancellationToken)
        {
            var ffprobePath = await ffmpegService.GetFfprobePathAsync();
            if (string.IsNullOrEmpty(ffprobePath))
            {
                return new ProbeOutcome(null, "ffprobe is unavailable.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("warning");
            startInfo.ArgumentList.Add("-print_format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("-show_format");
            startInfo.ArgumentList.Add("-show_chapters");
            startInfo.ArgumentList.Add("-show_streams");
            startInfo.ArgumentList.Add(Path.GetFullPath(path));

            var probe = await processRunner.RunAsync(startInfo, ProbeTimeoutMs, cancellationToken);
            if (probe.TimedOut || probe.ExitCode != 0)
            {
                return new ProbeOutcome(null, FfmpegService.SummariseFfprobeFailure(probe.Stderr));
            }

            try
            {
                using var document = JsonDocument.Parse(probe.Stdout);
                return new ProbeOutcome(Map(document.RootElement), null);
            }
            catch (JsonException ex)
            {
                return new ProbeOutcome(null, ex.Message);
            }
        }

        private static AudiobookFileTags Map(JsonElement root)
        {
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duration = TimeSpan.Zero;

            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("tags", out var formatTags)
                    && formatTags.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in formatTags.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        // First value wins. ffprobe reports a container carrying the same
                        // key twice as two properties, and several files in this library
                        // do carry a duplicate SERIES.
                        tags.TryAdd(property.Name, property.Value.GetString() ?? string.Empty);
                    }
                }

                if (format.TryGetProperty("duration", out var durationValue)
                    && double.TryParse(
                        durationValue.GetString(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var seconds))
                {
                    duration = TimeSpan.FromSeconds(seconds);
                }
            }

            var chapterCount = root.TryGetProperty("chapters", out var chapters)
                ? chapters.GetArrayLength()
                : 0;

            var hasCoverArt = false;
            if (root.TryGetProperty("streams", out var streams)
                && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (!stream.TryGetProperty("codec_type", out var codecType)
                        || codecType.GetString() != "video")
                    {
                        continue;
                    }

                    // A video stream is a cover only when it is flagged as one. An M4B
                    // that genuinely carries video is not something to re-attach as art.
                    if (stream.TryGetProperty("disposition", out var disposition)
                        && disposition.TryGetProperty("attached_pic", out var attached)
                        && attached.ValueKind == JsonValueKind.Number
                        && attached.GetInt32() == 1)
                    {
                        hasCoverArt = true;
                        break;
                    }
                }
            }

            return new AudiobookFileTags(tags, chapterCount, duration, hasCoverArt);
        }

        private async Task<(int ExitCode, string Stderr)> RunFfmpegAsync(
            string ffmpegPath,
            IReadOnlyList<string> arguments,
            TimeSpan total,
            IProgress<double>? progress,
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

            // ArgumentList, never a joined string: paths come from the library and tag
            // values from a metadata provider, and both may hold quotes or spaces. The
            // list form is passed to exec verbatim.
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
                    // this without limit.
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
        /// Read ffmpeg's <c>-progress</c> stream. It emits <c>key=value</c> lines; the
        /// useful field is <c>out_time_us</c>, the position reached in the output.
        /// </summary>
        private static async Task PumpProgressAsync(
            Process process,
            TimeSpan total,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            if (progress == null)
            {
                // Still drain it: a full pipe buffer would stall ffmpeg.
                await process.StandardOutput.ReadToEndAsync(cancellationToken);
                return;
            }

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
            {
                var separator = line.IndexOf('=');
                if (separator <= 0 || !line.AsSpan(0, separator).SequenceEqual("out_time_us"))
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

                var copied = TimeSpan.FromMilliseconds(microseconds / 1000.0);
                progress.Report(total > TimeSpan.Zero
                    ? Math.Clamp(copied.TotalSeconds / total.TotalSeconds, 0, 1)
                    : 0);
            }
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
