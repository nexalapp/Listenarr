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
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Common
{
    /// <summary>
    /// Real M4B files, synthesised with the ffmpeg on PATH. Guard callers with
    /// <see cref="EncoderFactAttribute"/>; there are no binary fixtures checked in.
    /// </summary>
    public static class M4bFixtures
    {
        public static string? Ffmpeg => EncoderFactAttribute.FindOnPath("ffmpeg");

        public static string? Ffprobe => EncoderFactAttribute.FindOnPath("ffprobe");

        public static async Task RunFfmpegAsync(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Ffmpeg ?? throw new InvalidOperationException("ffmpeg is not on PATH"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }

            var runner = new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance);
            var result = await runner.RunAsync(startInfo, 60_000);
            Assert.True(result.ExitCode == 0, result.Stderr);
        }

        /// <summary>
        /// A sine-tone M4B, optionally chaptered (evenly, titled through <paramref name="title"/>),
        /// optionally with cover art and extra ffmetadata.
        /// </summary>
        public static async Task<string> WriteBookAsync(
            string directory,
            string name = "book.m4b",
            int seconds = 4,
            int chapters = 0,
            bool coverArt = false,
            Func<int, string>? title = null,
            string? extraMetadataDocument = null)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);

            var arguments = new List<string>
            {
                "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}"
            };

            if (coverArt)
            {
                arguments.AddRange(["-f", "lavfi", "-i", "color=c=red:s=64x64:d=1"]);
            }

            string? metadataPath = null;
            if (chapters > 0 || extraMetadataDocument != null)
            {
                metadataPath = Path.Combine(directory, name + ".ffmetadata");
                var builder = new System.Text.StringBuilder(";FFMETADATA1\n");
                if (extraMetadataDocument != null)
                {
                    builder.Append(extraMetadataDocument);
                }

                var slice = seconds * 1000 / Math.Max(chapters, 1);
                for (var i = 0; i < chapters; i++)
                {
                    builder.Append("[CHAPTER]\nTIMEBASE=1/1000\n");
                    builder.Append($"START={i * slice}\nEND={(i + 1) * slice}\n");
                    builder.Append($"title={title?.Invoke(i + 1) ?? $"Chapter {i + 1}"}\n");
                }

                await File.WriteAllTextAsync(metadataPath, builder.ToString());
                arguments.AddRange(["-i", metadataPath]);
            }

            arguments.AddRange(["-map", "0:a"]);
            if (coverArt)
            {
                arguments.AddRange(["-map", "1:v", "-c:v", "mjpeg", "-frames:v", "1", "-disposition:v", "attached_pic"]);
            }

            if (metadataPath != null)
            {
                arguments.AddRange(["-map_metadata", coverArt ? "2" : "1"]);
            }

            arguments.AddRange(["-c:a", "aac", "-b:a", "64k", "-f", "ipod", path]);

            await RunFfmpegAsync([.. arguments]);
            return path;
        }

        /// <summary>
        /// Reproduce the TagLib# UnknownBox bug on a copy: the <c>chpl</c> payload is
        /// replaced with the bytes 24 further along, and the atom keeps its size.
        /// </summary>
        public static void ShiftChapterAtom(string path, int shift = 24)
        {
            var bytes = File.ReadAllBytes(path);
            var atom = IndexOf(bytes, "chpl"u8);
            Assert.True(atom >= 0, "the fixture has no chpl atom");

            var payloadStart = atom + 4;
            var size = (bytes[atom - 4] << 24) | (bytes[atom - 3] << 16) | (bytes[atom - 2] << 8) | bytes[atom - 1];
            var payloadLength = size - 8;
            var shifted = new byte[payloadLength];
            var available = Math.Min(payloadLength, bytes.Length - (payloadStart + shift));
            Array.Copy(bytes, payloadStart + shift, shifted, 0, Math.Max(available, 0));
            Array.Copy(shifted, 0, bytes, payloadStart, payloadLength);
            File.WriteAllBytes(path, bytes);
        }

        public static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) =>
            haystack.AsSpan().IndexOf(needle);

        /// <summary>An <see cref="IFfmpegService"/> that answers with the binaries on PATH and nothing else.</summary>
        public static IFfmpegService PathResolvedFfmpeg() => new PathResolvedFfmpegService(Ffmpeg, Ffprobe);

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

            public Task<TimeSpan?> MeasureDecodedDurationAsync(
                string filePath,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<TimeSpan?>(null);

            public Task<AudioMetadata> RunFfprobeAsync(string filePath) =>
                throw new NotSupportedException();

            public Task<AudioMetadata> RunFfprobeAsync(MetadataFileSource fileSource) =>
                throw new NotSupportedException();
        }
    }
}
