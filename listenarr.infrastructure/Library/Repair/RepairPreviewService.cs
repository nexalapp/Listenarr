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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Listenarr.Application.Audiobooks.Repair;
using Listenarr.Domain.Audiobooks.Repair;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Repair
{
    /// <summary>
    /// Runs one window of one file through the exact per-chunk pipeline a full repair
    /// uses — extract, enhance under the gate, loudness-matched encode — so what the
    /// listener approves is what the repair will do.
    /// </summary>
    public sealed class RepairPreviewService(
        IServiceScopeFactory scopeFactory,
        IAudioEnhancer enhancer,
        EnhancerGate gate,
        IApplicationPathService paths,
        ILogger<RepairPreviewService> logger) : IRepairPreviewService
    {
        /// <summary>Long enough to judge intelligibility and background, short enough to answer in ~2 minutes.</summary>
        public static readonly TimeSpan WindowLength = TimeSpan.FromSeconds(90);

        private readonly ConcurrentDictionary<Guid, RepairPreview> _previews = new();
        private readonly ConcurrentDictionary<int, Guid> _latestByAudiobook = new();

        public async Task<RepairPreview> StartAsync(
            int audiobookId,
            int? fileId = null,
            double? startSeconds = null,
            CancellationToken cancellationToken = default)
        {
            if (!await enhancer.IsAvailableAsync(cancellationToken))
            {
                var status = await enhancer.GetStatusAsync(cancellationToken);
                throw new EnhancerUnavailableException(status.State switch
                {
                    EnhancerState.Provisioning =>
                        $"The enhancement model is still downloading ({status.ProvisionProgress:0}%).",
                    EnhancerState.Failed => $"The enhancement model could not be provisioned: {status.Error}",
                    _ => "The enhancement model is not provisioned. Enabling repair in Settings downloads it."
                });
            }

            using var scope = scopeFactory.CreateScope();
            var audiobook = await scope.ServiceProvider.GetRequiredService<IAudiobookRepository>()
                .GetByIdAsync(audiobookId)
                ?? throw new InvalidOperationException("That audiobook no longer exists.");

            var candidates = (audiobook.Files ?? [])
                .Where(file => RepairableSource.IsRepairable(file.Path))
                .Select(file => (File: file, FullPath: AudiobookFilePaths.ResolveFullPath(audiobook, file)))
                .Where(pair => pair.FullPath != null && File.Exists(pair.FullPath))
                .ToList();
            var chosen = fileId != null
                ? candidates.FirstOrDefault(pair => pair.File.Id == fileId)
                : candidates.OrderByDescending(pair => pair.File.DurationSeconds ?? 0).FirstOrDefault();
            if (chosen.FullPath == null)
            {
                throw new InvalidOperationException("This book has no audio file here to preview.");
            }

            var duration = chosen.File.DurationSeconds ?? 0;
            var start = Math.Max(0, startSeconds
                // The midpoint: far from intro music at the front and the publisher
                // outro at the back, so the window is the narration being judged.
                ?? Math.Max(0, duration / 2 - WindowLength.TotalSeconds / 2));
            if (duration > 0)
            {
                start = Math.Min(start, Math.Max(0, duration - WindowLength.TotalSeconds));
            }

            var preview = new RepairPreview(
                Guid.NewGuid(), audiobookId, RepairPreviewState.Pending,
                "Waiting for the enhancer.", null, null);
            _previews[preview.Id] = preview;
            if (_latestByAudiobook.TryGetValue(audiobookId, out var previous))
            {
                _previews.TryRemove(previous, out _);
            }

            _latestByAudiobook[audiobookId] = preview.Id;

            // Fire and forget on purpose; the caller polls. Never the request's token —
            // a preview a listener asked for should finish even if their tab blinked.
            _ = Task.Run(() => RunPreviewAsync(preview.Id, audiobookId, chosen.FullPath!, start), CancellationToken.None);
            return preview;
        }

        public Task<RepairPreview?> GetAsync(Guid previewId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_previews.TryGetValue(previewId, out var preview) ? preview : null);

        public string? GetClipPath(Guid previewId, string? variant)
        {
            if (!_previews.TryGetValue(previewId, out var preview) || preview.State != RepairPreviewState.Ready)
            {
                return null;
            }

            var path = variant?.ToLowerInvariant() switch
            {
                "before" => preview.BeforePath,
                "after" => preview.AfterPath,
                _ => null
            };
            return path != null && File.Exists(path) ? path : null;
        }

        private async Task RunPreviewAsync(Guid previewId, int audiobookId, string sourcePath, double startSeconds)
        {
            var previewDirectory = Path.Combine(
                paths.ResolveFromConfig("conversion"), "repair-previews", previewId.ToString("N"));
            try
            {
                Directory.CreateDirectory(previewDirectory);
                using var scope = scopeFactory.CreateScope();
                var ffmpeg = await scope.ServiceProvider.GetRequiredService<IFfmpegService>().GetFfmpegPathAsync();
                if (string.IsNullOrEmpty(ffmpeg))
                {
                    Update(previewId, state => state with
                    {
                        State = RepairPreviewState.Failed,
                        Detail = "No ffmpeg is installed."
                    });
                    return;
                }

                var originalWav = Path.Combine(previewDirectory, "original.wav");
                await ExtractWindowAsync(ffmpeg, sourcePath, startSeconds, originalWav);

                Update(previewId, state => state with
                {
                    State = RepairPreviewState.Enhancing,
                    Detail = "Enhancing the window. A running repair's current chunk may be ahead of it."
                });

                var enhancedWav = Path.Combine(previewDirectory, "enhanced.wav");
                using (await gate.EnterAsync(CancellationToken.None))
                {
                    await enhancer.EnhanceChunkAsync(originalWav, enhancedWav, WindowLength, CancellationToken.None);
                }

                // Both sides through the identical final encode, so the A/B hears the
                // enhancement and not the codec.
                var beforePath = Path.Combine(previewDirectory, "before.m4a");
                var afterPath = Path.Combine(previewDirectory, "after.m4a");
                await EncodeClipAsync(ffmpeg, originalWav, beforePath);
                await EncodeClipAsync(ffmpeg, enhancedWav, afterPath);
                File.Delete(originalWav);
                File.Delete(enhancedWav);

                Update(previewId, state => state with
                {
                    State = RepairPreviewState.Ready,
                    Detail = null,
                    BeforePath = beforePath,
                    AfterPath = afterPath
                });
            }
            catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
            {
                logger.LogWarning(exception, "A repair preview for audiobook {AudiobookId} failed", audiobookId);
                Update(previewId, state => state with
                {
                    State = RepairPreviewState.Failed,
                    Detail = exception.Message
                });
                try
                {
                    Directory.Delete(previewDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        private void Update(Guid previewId, Func<RepairPreview, RepairPreview> change)
        {
            if (_previews.TryGetValue(previewId, out var current))
            {
                _previews[previewId] = change(current);
            }
        }

        private static async Task ExtractWindowAsync(string ffmpeg, string sourcePath, double startSeconds, string outputPath)
        {
            await RunAsync(ffmpeg,
            [
                "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
                "-ss", startSeconds.ToString("F2", CultureInfo.InvariantCulture),
                "-i", sourcePath,
                "-t", WindowLength.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture),
                "-vn", "-ac", "1", "-ar", RepairPlanPolicy.SampleRate.ToString(), "-c:a", "pcm_s16le",
                outputPath
            ]);
        }

        private static async Task EncodeClipAsync(string ffmpeg, string inputPath, string outputPath)
        {
            await RunAsync(ffmpeg,
            [
                "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
                "-i", inputPath,
                "-c:a", "aac", "-b:a", (RepairPlanPolicy.BitRate / 1000) + "k",
                "-movflags", "+faststart",
                outputPath
            ]);
        }

        private static async Task RunAsync(string ffmpeg, string[] arguments)
        {
            var startInfo = new ProcessStartInfo
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

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("ffmpeg could not be started.");
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
            {
                var detail = (await stderr).Trim();
                throw new InvalidOperationException(
                    $"ffmpeg failed (exit {process.ExitCode}): {(detail.Length <= 300 ? detail : detail[^300..])}");
            }
        }
    }
}
