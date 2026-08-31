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
using System.Text.Json;

namespace Listenarr.Infrastructure.Ffmpeg.Tagging
{
    /// <summary>
    /// Reads a container's tags, chapter count, duration and cover art with ffprobe.
    ///
    /// <para>
    /// Deliberately ffprobe rather than the same library that writes the tags. A writer
    /// that verified its own work with its own parser would agree with itself about a
    /// value it had misplaced; ffprobe is the reader whose view of the file matches what
    /// a player sees, which is the only view that decides whether this worked.
    /// </para>
    /// </summary>
    public sealed class FfprobeTagReader(
        IFfmpegService ffmpegService,
        IProcessRunner processRunner)
    {
        /// <summary>
        /// Long enough for ffprobe to read a book-sized file across a NAS share that has
        /// gone to sleep, short enough that a hung probe does not hold a lease forever.
        /// </summary>
        private const int ProbeTimeoutMs = 120_000;

        public sealed record ProbeOutcome(AudiobookFileTags? Tags, string? Error);

        public async Task<bool> IsAvailableAsync()
        {
            var ffprobe = await ffmpegService.GetFfprobePathAsync();
            return !string.IsNullOrEmpty(ffprobe);
        }

        public async Task<ProbeOutcome> ProbeAsync(string path, CancellationToken cancellationToken)
        {
            var ffprobePath = await ffmpegService.GetFfprobePathAsync();
            if (string.IsNullOrEmpty(ffprobePath))
            {
                return new ProbeOutcome(null, "ffprobe is unavailable.");
            }

            if (!File.Exists(path))
            {
                return new ProbeOutcome(
                    null,
                    $"The file is missing: {LogRedaction.SanitizeFilePath(path)}");
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

        internal static AudiobookFileTags Map(JsonElement root)
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

            tags.TryGetValue("major_brand", out var majorBrand);
            return new AudiobookFileTags(tags, chapterCount, duration, hasCoverArt, majorBrand);
        }
    }
}
