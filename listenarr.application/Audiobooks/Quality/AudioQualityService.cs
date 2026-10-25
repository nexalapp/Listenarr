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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Domain.Audiobooks.Quality;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Quality
{
    /// <summary>
    /// Probes a book by decoding a few windows of its longest file — the file that
    /// carries the recording; a short companion file would judge the intro music.
    /// Minutes of audio stand in for hours because the defects this looks for belong to
    /// the medium or the encode, which are the same in chapter 2 and chapter 30; the one
    /// that varies (a production's score) is exactly what the window spread detects.
    /// </summary>
    public sealed class AudioQualityService(
        IAudiobookRepository audiobookRepository,
        ITagQueueService tagQueue,
        IConfigurationService configurationService,
        IAudioSampleReader sampleReader,
        IFileSystem fileSystem,
        ILogger<AudioQualityService> logger) : IAudioQualityService
    {
        /// <summary>
        /// Ten windows of fifteen seconds, the research's sampling. Four were too few for a
        /// spread: one scene change among four windows moves the deviation by several dB.
        /// </summary>
        private const int WindowCount = 10;
        private const double WindowSeconds = 15;

        public async Task<TagEnqueueResult> EnqueueAsync(int audiobookId, TagTrigger trigger, CancellationToken cancellationToken = default)
        {
            if (trigger != TagTrigger.Manual)
            {
                var settings = await configurationService.GetApplicationSettingsAsync();
                if (!settings.AudioQualityProbeEnabled)
                {
                    return new TagEnqueueResult(TagEnqueueOutcome.Disabled, Reason: "Probing audio quality is switched off.");
                }
            }

            return await tagQueue.EnqueueAudioQualityProbeAsync(audiobookId, trigger, cancellationToken);
        }

        public async Task<AudioQualityReport> ProbeAsync(int audiobookId, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId)
                ?? throw new InvalidOperationException("That audiobook no longer exists.");

            var files = (audiobook.Files ?? [])
                .Where(file => FileUtils.IsAudioFile(file.Path ?? string.Empty))
                .Select(file => (File: file, FullPath: AudiobookFilePaths.ResolveFullPath(audiobook, file)))
                .Where(pair => pair.FullPath != null && fileSystem.FileExists(pair.FullPath))
                .ToList();

            if (files.Count == 0)
            {
                // Recorded, not thrown: a sweep that kept finding this book would retry
                // it every tick forever. The identity pins the answer to the files as
                // they stand; a rescan after they change probes again.
                return await StoreAsync(audiobookId, audiobook, Unknown("This book has no audio files here to probe."), cancellationToken);
            }

            var longest = files
                .OrderByDescending(pair => pair.File.DurationSeconds ?? 0)
                .ThenBy(pair => pair.File.Path, StringComparer.OrdinalIgnoreCase)
                .First();

            progress?.Report(10);
            var samples = await sampleReader.ReadWindowsAsync(
                longest.FullPath!,
                longest.File.DurationSeconds ?? 0,
                WindowCount,
                WindowSeconds,
                cancellationToken);
            if (samples == null || samples.Windows.Count == 0)
            {
                return await StoreAsync(audiobookId, audiobook, Unknown("None of the sampled windows could be decoded."), cancellationToken);
            }

            progress?.Report(70);
            var settings = await configurationService.GetApplicationSettingsAsync();
            var thresholds = AudioQualityThresholds.Default with
            {
                GenerativeDeficitDb = settings.GenerativeOfferThresholdDb
            };

            var report = AudioQualityProbe.Analyze(
                samples.Windows, samples.SampleRate, longest.File.Bitrate, thresholds);
            report = await StoreAsync(audiobookId, audiobook, report, cancellationToken);
            progress?.Report(100);
            return report;
        }

        private async Task<AudioQualityReport> StoreAsync(
            int audiobookId,
            Audiobook audiobook,
            AudioQualityReport report,
            CancellationToken cancellationToken)
        {
            var identity = AudioQualityIdentity.Of((audiobook.Files ?? [])
                .Where(file => FileUtils.IsAudioFile(file.Path ?? string.Empty))
                .Select(file => file.DurationSeconds ?? 0));
            await audiobookRepository.SetAudioQualityAsync(
                audiobookId,
                new AudioQualityRecord(
                    report.Verdict,
                    report.Reason,
                    System.Text.Json.JsonSerializer.Serialize(report.Metrics),
                    identity,
                    DateTime.UtcNow),
                cancellationToken);

            logger.LogInformation(
                "Audio quality probe for audiobook {AudiobookId}: {Verdict} — {Reason}",
                audiobookId,
                report.Verdict,
                report.Reason);
            return report;
        }

        private static AudioQualityReport Unknown(string reason) => new(
            AudioQuality.Unknown,
            reason,
            new AudioQualityMetrics(0, null, 0, 0, 0, null, 0),
            GenerativeRecommended: false,
            NeverPreDenoise: false);
    }
}
