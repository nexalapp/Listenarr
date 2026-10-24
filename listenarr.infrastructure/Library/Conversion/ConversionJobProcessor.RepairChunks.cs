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
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Listenarr.Domain.Audiobooks.Repair;
using Microsoft.Extensions.DependencyInjection;

namespace Listenarr.Infrastructure.Library.Conversion
{
    public sealed partial class ConversionJobProcessor
    {
        private sealed class ChunkManifest
        {
            public string SourcePath { get; set; } = string.Empty;
            public long SourceLength { get; set; }
            public DateTime SourceLastWriteUtc { get; set; }
            public string EnhancerVersion { get; set; } = string.Empty;
            public int ChunkCount { get; set; }
            public HashSet<int> Done { get; set; } = [];
        }

        private static async Task<ChunkManifest> LoadOrStartChunkManifestAsync(
            string workDirectory,
            ConversionSource source,
            string pipelineVersion,
            int chunkCount)
        {
            var manifestPath = Path.Combine(workDirectory, "manifest.json");
            var info = new FileInfo(source.FullPath);
            if (File.Exists(manifestPath))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<ChunkManifest>(await File.ReadAllTextAsync(manifestPath));
                    if (loaded != null
                        && loaded.SourcePath == source.FullPath
                        && loaded.SourceLength == info.Length
                        && loaded.SourceLastWriteUtc == info.LastWriteTimeUtc
                        && loaded.EnhancerVersion == pipelineVersion
                        && loaded.ChunkCount == chunkCount)
                    {
                        return loaded;
                    }
                }
                catch (JsonException)
                {
                }

                // The chunks on disk are chunks of something else now. Start over.
                foreach (var stale in Directory.EnumerateFiles(workDirectory))
                {
                    File.Delete(stale);
                }
            }

            var fresh = new ChunkManifest
            {
                SourcePath = source.FullPath,
                SourceLength = info.Length,
                SourceLastWriteUtc = info.LastWriteTimeUtc,
                EnhancerVersion = pipelineVersion,
                ChunkCount = chunkCount
            };
            await SaveChunkManifestAsync(workDirectory, fresh);
            return fresh;
        }

        private static async Task SaveChunkManifestAsync(string workDirectory, ChunkManifest manifest)
        {
            var manifestPath = Path.Combine(workDirectory, "manifest.json");
            var temporary = manifestPath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(manifest));
            File.Move(temporary, manifestPath, overwrite: true);
        }

        /// <summary>
        /// One chunk of the source as mono 44.1 kHz WAV — the model's own working format.
        /// Mono is deliberate: the model is single-channel (a stereo input is downmixed
        /// inside it anyway), and the cassette-era books this exists for are mono or
        /// dual-mono. The repaired book is mono by construction.
        /// </summary>
        private async Task ExtractChunkAsync(
            string ffmpeg,
            string sourcePath,
            RepairChunk chunk,
            string outputPath,
            CancellationToken cancellationToken)
        {
            var exitCode = await RunFfmpegAsync(
                ffmpeg,
                [
                    "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
                    "-ss", chunk.Start.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
                    "-i", sourcePath,
                    "-t", chunk.Length.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
                    "-vn", "-ac", "1", "-ar", RepairPlanPolicy.SampleRate.ToString(), "-c:a", "pcm_s16le",
                    outputPath
                ],
                TimeSpan.FromMinutes(20),
                cancellationToken);
            if (exitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                throw new InvalidOperationException(
                    $"A chunk of {Path.GetFileName(sourcePath)} could not be decoded (ffmpeg exit {exitCode}).");
            }
        }

        private async Task ConcatChunksAsync(
            string ffmpeg,
            IReadOnlyList<string> chunkPaths,
            string outputPath,
            CancellationToken cancellationToken)
        {
            var listPath = outputPath + ".list";
            await File.WriteAllLinesAsync(
                listPath,
                chunkPaths.Select(path => $"file '{path.Replace("'", "'\\''")}'"),
                cancellationToken);
            try
            {
                // The concat DEMUXER is refused for conversion because it adopts the first
                // input's format for mixed sources; these chunks are all the same format
                // by construction, which is the one case it is exact and cheap for.
                var exitCode = await RunFfmpegAsync(
                    ffmpeg,
                    [
                        "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
                        "-f", "concat", "-safe", "0", "-i", listPath,
                        "-c:a", "flac",
                        outputPath
                    ],
                    TimeSpan.FromMinutes(30),
                    cancellationToken);
                if (exitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                {
                    throw new InvalidOperationException($"The enhanced chunks could not be reassembled (ffmpeg exit {exitCode}).");
                }
            }
            finally
            {
                TryDeleteScratch(listPath);
            }
        }

        [GeneratedRegex(@"mean_volume:\s*(?<db>-?[0-9.]+)\s*dB")]
        private static partial Regex MeanVolume();

        /// <summary>Mean RMS of a chunk in dBFS, via ffmpeg's volumedetect; null when unreadable.</summary>
        private async Task<double?> MeasureRmsDbAsync(string ffmpeg, string path, CancellationToken cancellationToken)
        {
            var (exitCode, stderr) = await RunFfmpegCapturingAsync(
                ffmpeg,
                [
                    "-hide_banner", "-nostdin", "-i", path,
                    "-af", "volumedetect", "-f", "null", "-"
                ],
                TimeSpan.FromMinutes(10),
                cancellationToken);
            if (exitCode != 0)
            {
                return null;
            }

            var match = MeanVolume().Match(stderr);
            return match.Success && double.TryParse(
                match.Groups["db"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var db)
                ? db
                : null;
        }

        private async Task<TimeSpan?> MeasureDecodedDurationAsync(string path, CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IFfmpegService>()
                .MeasureDecodedDurationAsync(path, cancellationToken);
        }

        private async Task<int> RunFfmpegAsync(
            string ffmpeg,
            string[] arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var (exitCode, _) = await RunFfmpegCapturingAsync(ffmpeg, arguments, timeout, cancellationToken);
            return exitCode;
        }

        private async Task<(int ExitCode, string Stderr)> RunFfmpegCapturingAsync(
            string ffmpeg,
            string[] arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("ffmpeg could not be started.");
            var stderr = process.StandardError.ReadToEndAsync(linked.Token);
            _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, linked.Token);
            await process.WaitForExitAsync(linked.Token);
            return (process.ExitCode, await stderr);
        }
    }
}
