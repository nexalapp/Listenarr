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
using System.Text.RegularExpressions;
using Listenarr.Application.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Chapters;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Ffmpeg.Analysis
{
    /// <summary>
    /// Finds pauses with ffmpeg's <c>silencedetect</c> filter: one decode of the file to
    /// a null muxer, reading the filter's log lines off stderr as they arrive.
    ///
    /// <para>
    /// The noise floor is set for narration, not music: a narrator's room tone sits well
    /// under -35 dBFS while the quietest spoken word does not, so a pause is a stretch
    /// under that floor. The lines are read as a stream rather than after exit because a
    /// ten-hour book produces thousands of them and the process must be killable on
    /// cancellation without losing what was already heard.
    /// </para>
    /// </summary>
    public sealed partial class FfmpegSilenceDetector(
        IFfmpegService ffmpegService,
        IProcessRunner processRunner,
        ILogger<FfmpegSilenceDetector> logger) : ISilenceDetector
    {
        /// <summary>Below this is a pause. Narration's quietest syllables are louder; room tone is quieter.</summary>
        public const string NoiseFloor = "-35dB";

        /// <summary>A decode runs a long book in a few minutes; longer than this is a stuck decoder, not a long book.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

        [GeneratedRegex(@"silence_end:\s*(?<end>-?[0-9.]+)\s*\|\s*silence_duration:\s*(?<length>[0-9.]+)")]
        private static partial Regex SilenceEnd();

        public async Task<IReadOnlyList<SilenceSpan>> DetectAsync(string path, TimeSpan minimumLength, CancellationToken cancellationToken = default)
        {
            var ffmpeg = await ffmpegService.GetFfmpegPathAsync();
            if (string.IsNullOrEmpty(ffmpeg))
            {
                throw new SilenceDetectionException("No ffmpeg is installed, so the audio cannot be decoded.");
            }

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
                         "-hide_banner", "-nostdin", "-nostats", "-loglevel", "info",
                         "-i", path,
                         "-vn", "-af", $"silencedetect=noise={NoiseFloor}:d={Math.Max(0.1, minimumLength.TotalSeconds).ToString("F2", CultureInfo.InvariantCulture)}",
                         "-f", "null", "-"
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            var silences = new List<SilenceSpan>();
            var tail = new Queue<string>();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            Process process;
            try
            {
                process = processRunner.StartProcess(startInfo);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                throw new SilenceDetectionException($"ffmpeg could not be started: {ex.Message}", ex);
            }

            using (process)
            {
                var drain = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
                try
                {
                    while (await process.StandardError.ReadLineAsync(timeout.Token) is { } line)
                    {
                        if (Parse(line) is { } silence)
                        {
                            silences.Add(silence);
                        }
                        else
                        {
                            tail.Enqueue(line);
                            if (tail.Count > 20)
                            {
                                tail.Dequeue();
                            }
                        }
                    }

                    await process.WaitForExitAsync(timeout.Token);
                    await drain;
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    throw new SilenceDetectionException($"ffmpeg took longer than {Timeout.TotalMinutes:F0} minutes to decode {LogRedaction.SanitizeFilePath(path)}.");
                }

                if (process.ExitCode != 0)
                {
                    throw new SilenceDetectionException(
                        $"ffmpeg could not decode {LogRedaction.SanitizeFilePath(path)} (exit {process.ExitCode}): {string.Join(" ", tail).Trim()}");
                }
            }

            logger.LogDebug("{Count} pause(s) of at least {Minimum}s in {Path}", silences.Count, minimumLength.TotalSeconds, LogRedaction.SanitizeFilePath(path));
            return silences;
        }

        /// <summary>One silencedetect line: the pause it closes, or null for any other line.</summary>
        public static SilenceSpan? Parse(string line)
        {
            var match = SilenceEnd().Match(line);
            if (!match.Success
                || !double.TryParse(match.Groups["end"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var end)
                || !double.TryParse(match.Groups["length"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var length)
                || length <= 0)
            {
                return null;
            }

            var finish = TimeSpan.FromSeconds(Math.Max(0, end));
            var start = TimeSpan.FromSeconds(Math.Max(0, end - length));
            return new SilenceSpan(start, finish);
        }
    }
}
