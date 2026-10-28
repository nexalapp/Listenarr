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
using Microsoft.Extensions.Logging;

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

            /// <summary>The grid's chunk length; 0 in a manifest written before it was recorded.</summary>
            public int ChunkSeconds { get; set; }

            public HashSet<int> Done { get; set; } = [];

            public TimeSpan ChunkLength => ChunkSeconds > 0
                ? TimeSpan.FromSeconds(ChunkSeconds)
                : RepairChunkPlanner.LegacyChunkLength;

            public bool Describes(ConversionSource source, FileInfo info, string pipelineVersion) =>
                SourcePath == source.FullPath
                && SourceLength == info.Length
                && SourceLastWriteUtc == info.LastWriteTimeUtc
                && EnhancerVersion == pipelineVersion;
        }

        /// <summary>
        /// The chunk length to cut this file with: the one its manifest was started on,
        /// when that manifest still describes the file, so finished chunks are kept;
        /// otherwise the current one.
        /// </summary>
        internal static async Task<TimeSpan> ResumableChunkLengthAsync(
            string workDirectory,
            ConversionSource source,
            string pipelineVersion)
        {
            var manifest = await ReadChunkManifestAsync(workDirectory);
            return manifest != null && manifest.Describes(source, new FileInfo(source.FullPath), pipelineVersion)
                ? manifest.ChunkLength
                : RepairChunkPlanner.ChunkLength;
        }

        private static async Task<ChunkManifest?> ReadChunkManifestAsync(string workDirectory)
        {
            var manifestPath = Path.Combine(workDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ChunkManifest>(await File.ReadAllTextAsync(manifestPath));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static async Task<ChunkManifest> LoadOrStartChunkManifestAsync(
            string workDirectory,
            ConversionSource source,
            string pipelineVersion,
            TimeSpan chunkLength,
            int chunkCount)
        {
            var manifestPath = Path.Combine(workDirectory, "manifest.json");
            var info = new FileInfo(source.FullPath);
            if (File.Exists(manifestPath))
            {
                var loaded = await ReadChunkManifestAsync(workDirectory);
                if (loaded != null
                    && loaded.Describes(source, info, pipelineVersion)
                    && loaded.ChunkLength == chunkLength
                    && loaded.ChunkCount == chunkCount)
                {
                    return loaded;
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
                ChunkCount = chunkCount,
                ChunkSeconds = (int)chunkLength.TotalSeconds
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
            IReadOnlyList<(string Path, TimeSpan Length)> chunks,
            string outputPath,
            CancellationToken cancellationToken)
        {
            await NormalizeChunksAsync(ffmpeg, chunks, cancellationToken);
            var chunkPaths = chunks.Select(chunk => chunk.Path).ToList();

            var listPath = outputPath + ".list";
            await File.WriteAllLinesAsync(
                listPath,
                chunkPaths.Select(path => $"file '{path.Replace("'", "'\\''")}'"),
                cancellationToken);
            try
            {
                // The concat DEMUXER is refused for conversion because it adopts the first
                // input's format for mixed sources; the chunks were normalized to one
                // format above, which is the one case it is exact and cheap for.
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

        /// <summary>A WAV's sample format, read from its header.</summary>
        internal readonly record struct WaveFormat(int Format, int Channels, int SampleRate, int BitsPerSample)
        {
            /// <summary>What the local model writes, and so what every chunk is reassembled as: 32-bit float, mono, 44.1 kHz.</summary>
            public static readonly WaveFormat Canonical = new(3, 1, 44_100, 32);
        }

        /// <summary>A WAV's format and how long its samples run.</summary>
        internal readonly record struct WaveHeader(WaveFormat Format, TimeSpan Duration);

        /// <summary>A chunk may differ from its planned length by this much and still be joined as it is.</summary>
        private static readonly TimeSpan ChunkLengthTolerance = TimeSpan.FromMilliseconds(1);

        /// <summary>
        /// Bring every chunk to <see cref="WaveFormat.Canonical"/> and to its planned length
        /// before they are joined. Two assumptions behind a plain join failed on Xenocide.
        /// "Same format by construction": the music guard splices back an ORIGINAL chunk,
        /// which ffmpeg extracts as 16-bit, and the GPU worker's torch wrote 16-bit too; the
        /// concat demuxer reads every file in the first one's format, so each 16-bit chunk
        /// counted as half its length and the book came out 5.4 hours short. And "the
        /// model returns what it was given": the CPU model now and then returns 70–90 ms
        /// more than it was handed, which over sixty chunks ran 1.2 s long — past the
        /// half-second the chapter marks allow. Each chunk is cut or padded to the length
        /// the grid gave it, so the whole matches the source to the sample.
        /// </summary>
        private async Task NormalizeChunksAsync(
            string ffmpeg,
            IReadOnlyList<(string Path, TimeSpan Length)> chunks,
            CancellationToken cancellationToken)
        {
            var normalized = 0;
            foreach (var (path, length) in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var header = ReadWaveHeader(path);
                if (header is { } read
                    && read.Format == WaveFormat.Canonical
                    && (read.Duration - length).Duration() <= ChunkLengthTolerance)
                {
                    continue;
                }

                var converted = path + ".f32.wav";
                var exitCode = await RunFfmpegAsync(
                    ffmpeg,
                    [
                        "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
                        "-i", path,
                        // Padded with silence if short, then cut at exactly the planned length.
                        "-af", "apad",
                        "-t", length.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
                        "-c:a", "pcm_f32le", "-ar", "44100", "-ac", "1",
                        converted
                    ],
                    TimeSpan.FromMinutes(10),
                    cancellationToken);
                if (exitCode != 0 || ReadWaveHeader(converted) is not { } result || result.Format != WaveFormat.Canonical)
                {
                    TryDeleteScratch(converted);
                    throw new InvalidOperationException(
                        $"A chunk could not be brought to the common format (ffmpeg exit {exitCode}): {Path.GetFileName(path)}");
                }

                File.Move(converted, path, overwrite: true);
                normalized++;
            }

            if (normalized > 0)
            {
                logger.LogInformation(
                    "Brought {Count} of {Total} chunk(s) to 32-bit float at their planned length before reassembly",
                    normalized,
                    chunks.Count);
            }
        }

        /// <summary>The header's format and the length of its samples, or null when the file is not a readable WAV.</summary>
        internal static WaveHeader? ReadWaveHeader(string path)
        {
            const uint Riff = 0x46464952, Wave = 0x45564157, Fmt = 0x20746D66, Data = 0x61746164;
            const int Extensible = 0xFFFE;
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                if (stream.Length < 12 || reader.ReadUInt32() != Riff)
                {
                    return null;
                }

                reader.ReadUInt32();
                if (reader.ReadUInt32() != Wave)
                {
                    return null;
                }

                WaveFormat? format = null;
                var blockAlign = 0;
                while (stream.Position + 8 <= stream.Length)
                {
                    var id = reader.ReadUInt32();
                    var size = reader.ReadUInt32();
                    if (id == Data)
                    {
                        if (format is not { } known || blockAlign <= 0 || known.SampleRate <= 0)
                        {
                            return null;
                        }

                        // A streamed writer may leave the size unset; the rest of the file is the data then.
                        var available = stream.Length - stream.Position;
                        var bytes = size == 0 || size == uint.MaxValue || size > available ? available : size;
                        return new WaveHeader(known, TimeSpan.FromSeconds((double)(bytes / blockAlign) / known.SampleRate));
                    }

                    if (id != Fmt)
                    {
                        stream.Seek(size + (size & 1), SeekOrigin.Current);
                        continue;
                    }

                    if (size < 16)
                    {
                        return null;
                    }

                    var chunkEnd = stream.Position + size + (size & 1);
                    int tag = reader.ReadUInt16();
                    int channels = reader.ReadUInt16();
                    var sampleRate = (int)reader.ReadUInt32();
                    reader.ReadUInt32();
                    blockAlign = reader.ReadUInt16();
                    int bits = reader.ReadUInt16();
                    if (tag == Extensible && size >= 40)
                    {
                        // cbSize, valid bits, channel mask, then the sub-format GUID whose
                        // first two bytes are the real format tag.
                        reader.ReadUInt16();
                        reader.ReadUInt16();
                        reader.ReadUInt32();
                        tag = reader.ReadUInt16();
                    }

                    format = new WaveFormat(tag, channels, sampleRate, bits);
                    stream.Seek(chunkEnd, SeekOrigin.Begin);
                }

                return null;
            }
            catch (IOException)
            {
                return null;
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
