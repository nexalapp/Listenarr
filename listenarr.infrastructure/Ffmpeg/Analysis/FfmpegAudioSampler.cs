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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Ffmpeg.Analysis
{
    /// <summary>
    /// Decodes short windows spread through a file by seeking: <c>-ss</c> before
    /// <c>-i</c>, one ffmpeg run per window, raw mono floats off stdout. Four windows of
    /// thirty seconds cost a couple of seconds each against a NAS share, where decoding
    /// the whole book to find the same answer costs minutes.
    /// </summary>
    public sealed class FfmpegAudioSampler(
        IFfmpegService ffmpegService,
        IProcessRunner processRunner,
        ILogger<FfmpegAudioSampler> logger) : IAudioSampleReader
    {
        /// <summary>A thirty-second decode takes seconds; longer than this is a stuck decoder.</summary>
        public static readonly TimeSpan WindowTimeout = TimeSpan.FromMinutes(3);

        /// <summary>
        /// Windows sit at these fractions of the duration: clear of the intro music at
        /// the front and the publisher outro at the back, spread enough that a score's
        /// coming and going shows up as floor movement between them.
        /// </summary>
        private static readonly double[] Positions = [0.10, 0.35, 0.60, 0.85];

        public async Task<AudioSampleWindows?> ReadWindowsAsync(
            string path,
            double durationSeconds,
            int windowCount,
            double windowSeconds,
            CancellationToken cancellationToken = default)
        {
            var ffmpeg = await ffmpegService.GetFfmpegPathAsync();
            if (string.IsNullOrEmpty(ffmpeg))
            {
                // An environment fault, not a fact about the file: the caller retries
                // once ffmpeg has landed, where a null is recorded as the file's answer.
                throw new InvalidOperationException("No ffmpeg is installed, so the audio cannot be decoded.");
            }

            // The rate is the file's own: resampling would move the very bands being
            // measured.
            var sampleRate = await ReadSampleRateAsync(ffmpeg, path, cancellationToken);
            if (sampleRate <= 0)
            {
                return null;
            }

            // A file shorter than the full spread still gets judged: the windows shrink
            // toward reading the whole thing once from the start.
            var starts = new List<double>();
            if (durationSeconds > windowSeconds * (windowCount + 1))
            {
                for (var i = 0; i < Math.Min(windowCount, Positions.Length); i++)
                {
                    starts.Add(durationSeconds * Positions[i]);
                }
            }
            else
            {
                starts.Add(0);
                windowSeconds = Math.Max(windowSeconds, Math.Min(durationSeconds, 120));
            }

            var windows = new List<float[]>();
            foreach (var start in starts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var window = await DecodeWindowAsync(ffmpeg, path, start, windowSeconds, cancellationToken);
                if (window is { Length: > 0 })
                {
                    windows.Add(window);
                }
            }

            return windows.Count == 0
                ? null
                : new AudioSampleWindows(windows, sampleRate);
        }

        private async Task<float[]?> DecodeWindowAsync(
            string ffmpeg,
            string path,
            double startSeconds,
            double windowSeconds,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
                     {
                         "-hide_banner", "-nostdin", "-nostats", "-loglevel", "error",
                         "-ss", startSeconds.ToString("F2", CultureInfo.InvariantCulture),
                         "-i", path,
                         "-t", windowSeconds.ToString("F2", CultureInfo.InvariantCulture),
                         "-vn", "-ac", "1", "-f", "f32le", "-"
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(WindowTimeout);
            try
            {
                using var process = processRunner.StartProcess(startInfo);
                using var buffer = new MemoryStream();
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.StandardOutput.BaseStream.CopyToAsync(buffer, timeout.Token);
                await process.WaitForExitAsync(timeout.Token);

                if (process.ExitCode != 0 && buffer.Length == 0)
                {
                    logger.LogDebug(
                        "ffmpeg could not decode {Seconds}s at {Start}s of {Path}: {Error}",
                        windowSeconds,
                        startSeconds,
                        path,
                        Tail(await stderr));
                    return null;
                }

                var bytes = buffer.GetBuffer();
                var count = (int)(buffer.Length / sizeof(float));
                var samples = new float[count];
                Buffer.BlockCopy(bytes, 0, samples, 0, count * sizeof(float));
                return samples;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Decoding a window of {Path} did not finish inside {Timeout}", path, WindowTimeout);
                return null;
            }
        }

        private async Task<int> ReadSampleRateAsync(string ffmpeg, string path, CancellationToken cancellationToken)
        {
            // ffprobe ships beside ffmpeg in every install this app performs.
            var ffprobe = Path.Combine(
                Path.GetDirectoryName(ffmpeg) ?? string.Empty,
                Path.GetFileName(ffmpeg).Replace("ffmpeg", "ffprobe"));
            if (!File.Exists(ffprobe))
            {
                return 0;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
                     {
                         "-v", "error", "-select_streams", "a:0",
                         "-show_entries", "stream=sample_rate",
                         "-of", "default=noprint_wrappers=1:nokey=1",
                         path
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = processRunner.StartProcess(startInfo);
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return int.TryParse(output.Trim(), out var rate) ? rate : 0;
        }

        private static string Tail(string text)
        {
            var trimmed = text.Trim();
            return trimmed.Length <= 300 ? trimmed : trimmed[^300..];
        }
    }
}
