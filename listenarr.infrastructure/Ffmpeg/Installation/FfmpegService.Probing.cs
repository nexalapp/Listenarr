/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Ffmpeg.Installation
{
    public partial class FfmpegService : IFfmpegService
    {
        public Task<AudioMetadata> RunFfprobeAsync(string filePath)
        {
            return RunFfprobeAsync(new MetadataFileSource(filePath, filePath));
        }

        public async Task<AudioMetadata> RunFfprobeAsync(
            MetadataFileSource fileSource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileSource.ReadPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileSource.PublicPath);
            var sanitizedPublicPath = LogRedaction.SanitizeFilePath(
                fileSource.PublicPath);
            JsonElement ffprobeData;
            try
            {
                if (!File.Exists(_ffprobePath))
                {
                    throw new FfmpegException("ffprobe binary is unavailable.");
                }

                if (!FileSystemSafety.TryValidateMutationTarget(_ffprobePath, [_baseDir], out var safeFfprobePath, out var ffprobeReason))
                {
                    throw new FfmpegException($"ffprobe binary is unavailable or outside configured root: {LogRedaction.SanitizeText(ffprobeReason)}");
                }

                if (!File.Exists(fileSource.ReadPath))
                {
                    throw new FfmpegException($"ffprobe target does not exist: {sanitizedPublicPath}");
                }

                if (!FileUtils.IsAudioFile(fileSource.PublicPath))
                {
                    throw new FfmpegException($"ffprobe target is not a supported audio file: {sanitizedPublicPath}");
                }

                var safeReadPath = Path.GetFullPath(fileSource.ReadPath);
                _logger.LogInformation("Running bundled ffprobe at {Path} against file {File}", safeFfprobePath, sanitizedPublicPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = safeFfprobePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                // "warning", not "quiet" or "error": the messages that explain an
                // unreadable file (an unimplemented codec, most often) are logged by
                // ffmpeg at warning level, so any stricter level throws away the only
                // description of what went wrong. Diagnostics go to stderr, so the JSON
                // on stdout is unaffected.
                startInfo.ArgumentList.Add("-v");
                startInfo.ArgumentList.Add("warning");
                startInfo.ArgumentList.Add("-print_format");
                startInfo.ArgumentList.Add("json");
                startInfo.ArgumentList.Add("-show_format");
                startInfo.ArgumentList.Add("-show_streams");
                startInfo.ArgumentList.Add(safeReadPath);

                var pr = await _processRunner.RunAsync(startInfo, 10000);

                if (pr.TimedOut || pr.ExitCode != 0)
                {
                    var diagnostic = SummariseFfprobeFailure(pr.Stderr);
                    _logger.LogWarning(
                        "ffprobe exit code {Code} for file {File}{TimedOut}: {Diagnostic}",
                        pr.ExitCode,
                        sanitizedPublicPath,
                        pr.TimedOut ? " (timed out)" : string.Empty,
                        diagnostic);

                    // Carry the reason into the message: this is what reaches the import
                    // block detail, and "cannot read/process" on its own tells an operator
                    // nothing they can act on.
                    throw new FfmpegException(pr.TimedOut
                        ? $"ffprobe timed out reading {sanitizedPublicPath}"
                        : $"ffprobe cannot read/process {sanitizedPublicPath}: {diagnostic}");
                }

                _logger.LogInformation("ffprobe read {File} successfully", sanitizedPublicPath);

                if (string.IsNullOrEmpty(pr.Stdout))
                {
                    throw new FfmpegException($"Failed to parse ffprobe JSON output for {sanitizedPublicPath}: Cannot retrieve output or retrieved empty output");
                }

                try
                {
                    ffprobeData = JsonSerializer.Deserialize<JsonElement>(pr.Stdout);
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
                {
                    throw new FfmpegException($"Failed to parse ffprobe JSON output for {sanitizedPublicPath}", ex);
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new FfmpegException($"ffprobe execution failed for {sanitizedPublicPath}", ex);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                throw new FfmpegException($"Error running ffprobe for {sanitizedPublicPath}", ex);
            }

            var metadata = FfprobeMetadataMapper.Map(
                ffprobeData,
                fileSource.PublicPath);

            _logger.LogInformation("Extracted ffprobe metadata from file: {File}", sanitizedPublicPath);
            _logger.LogDebug("Parsed metadata: Duration={Duration} seconds, Format={Format}, Bitrate={Bitrate}, SampleRate={SampleRate}, Channels={Channels}", metadata.Duration.TotalSeconds, metadata.Format, metadata.BitRate, metadata.SampleRate, metadata.Channels);

            return metadata;
        }

        public async Task<IReadOnlyList<EmbeddedChapter>> ReadChaptersAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (!File.Exists(_ffprobePath) || !File.Exists(filePath))
            {
                return [];
            }

            if (!FileSystemSafety.TryValidateMutationTarget(
                    _ffprobePath,
                    [_baseDir],
                    out var safeFfprobePath,
                    out _))
            {
                return [];
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = safeFfprobePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-print_format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("-show_chapters");
            startInfo.ArgumentList.Add(Path.GetFullPath(filePath));

            ProcessResult probe;
            try
            {
                probe = await _processRunner.RunAsync(startInfo, 30_000, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(
                    ex,
                    "Could not read chapters from {File}",
                    LogRedaction.SanitizeFilePath(filePath));
                return [];
            }

            if (probe.TimedOut || probe.ExitCode != 0 || string.IsNullOrWhiteSpace(probe.Stdout))
            {
                return [];
            }

            try
            {
                using var document = JsonDocument.Parse(probe.Stdout);
                if (!document.RootElement.TryGetProperty("chapters", out var chapters))
                {
                    return [];
                }

                var results = new List<EmbeddedChapter>(chapters.GetArrayLength());
                foreach (var chapter in chapters.EnumerateArray())
                {
                    if (!TryReadSeconds(chapter, "start_time", out var start)
                        || !TryReadSeconds(chapter, "end_time", out var end))
                    {
                        continue;
                    }

                    string? title = null;
                    if (chapter.TryGetProperty("tags", out var tags)
                        && tags.TryGetProperty("title", out var titleValue))
                    {
                        title = titleValue.GetString();
                    }

                    results.Add(new EmbeddedChapter(
                        title,
                        TimeSpan.FromSeconds(start),
                        TimeSpan.FromSeconds(end)));
                }

                return results;
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(
                    ex,
                    "Could not parse chapters from {File}",
                    LogRedaction.SanitizeFilePath(filePath));
                return [];
            }
        }

        public async Task<TimeSpan?> MeasureDecodedDurationAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (!File.Exists(_ffmpegPath) || !File.Exists(filePath))
            {
                return null;
            }

            if (!FileSystemSafety.TryValidateMutationTarget(
                    _ffmpegPath,
                    [_baseDir],
                    out var safeFfmpegPath,
                    out _))
            {
                return null;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = safeFfmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-nostats");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(Path.GetFullPath(filePath));
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0:a:0");
            // The decoded time is read from the progress report, which ends with the
            // final out_time; the null muxer discards the samples themselves.
            startInfo.ArgumentList.Add("-progress");
            startInfo.ArgumentList.Add("pipe:1");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("null");
            startInfo.ArgumentList.Add("-");

            ProcessResult decode;
            try
            {
                decode = await _processRunner.RunAsync(startInfo, DecodeTimeoutMilliseconds, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Could not decode {File} to measure it", LogRedaction.SanitizeFilePath(filePath));
                return null;
            }

            if (decode.TimedOut || decode.ExitCode != 0 || string.IsNullOrWhiteSpace(decode.Stdout))
            {
                _logger.LogDebug(
                    "Decoding {File} to measure it ended with exit code {ExitCode}{TimedOut}",
                    LogRedaction.SanitizeFilePath(filePath),
                    decode.ExitCode,
                    decode.TimedOut ? " (timed out)" : string.Empty);
                return null;
            }

            return ParseFinalOutTime(decode.Stdout);
        }

        /// <summary>A day of audio at a slow decode; the encode that follows takes longer.</summary>
        private const int DecodeTimeoutMilliseconds = 60 * 60 * 1000;

        /// <summary>
        /// The last <c>out_time_us</c> line of an ffmpeg progress report, in microseconds
        /// (<c>out_time_ms</c> is misnamed and also in microseconds, kept as a fallback).
        /// </summary>
        internal static TimeSpan? ParseFinalOutTime(string progress)
        {
            long? micros = null;
            foreach (var rawLine in progress.Split('\n'))
            {
                var line = rawLine.Trim();
                if ((line.StartsWith("out_time_us=", StringComparison.Ordinal)
                        || line.StartsWith("out_time_ms=", StringComparison.Ordinal))
                    && long.TryParse(line[12..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    && value >= 0)
                {
                    micros = value;
                }
            }

            return micros.HasValue ? TimeSpan.FromMicroseconds(micros.Value) : null;
        }

        private static bool TryReadSeconds(JsonElement element, string property, out double seconds)
        {
            seconds = 0;
            if (!element.TryGetProperty(property, out var value))
            {
                return false;
            }

            // ffprobe writes these as strings, but a build that emits numbers should not
            // silently produce a book with no chapters.
            return value.ValueKind switch
            {
                JsonValueKind.String => double.TryParse(
                    value.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out seconds),
                JsonValueKind.Number => value.TryGetDouble(out seconds),
                _ => false
            };
        }

        public string FfprobePath
        {
            get
            {
                return _ffprobePath;
            }
        }

        public async Task<string> GetLicenseAsync()
        {
            var licensePath = Path.Join(_baseDir, "LICENSE_NOTICE.txt");
            if (System.IO.File.Exists(licensePath))
            {
                return await System.IO.File.ReadAllTextAsync(licensePath);
            }

            return string.Empty;
        }

        /// <summary>
        /// Reduce ffprobe's stderr to the one line worth showing an operator. ffmpeg
        /// repeats the same complaint once per decode attempt and prefixes each with a
        /// component and pointer, neither of which means anything outside ffmpeg.
        /// </summary>
        internal static string SummariseFfprobeFailure(string? stderr)
        {
            if (string.IsNullOrWhiteSpace(stderr))
            {
                return "no diagnostic output";
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var lines = new List<string>();
            foreach (var raw in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                // Strip a leading "[component @ 0xADDRESS] " prefix.
                var close = line.IndexOf("] ", StringComparison.Ordinal);
                if (line.StartsWith('[') && close > 0)
                {
                    line = line[(close + 2)..].Trim();
                }

                if (line.Length > 0 && seen.Add(line))
                {
                    lines.Add(line);
                }
            }

            if (lines.Count == 0)
            {
                return "no diagnostic output";
            }

            var summary = string.Join("; ", lines.Take(3));
            return summary.Length > 400 ? summary[..400] + "…" : summary;
        }

    }
}
