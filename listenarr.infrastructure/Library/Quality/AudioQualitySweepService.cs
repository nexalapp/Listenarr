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
using Listenarr.Domain.Audiobooks.Quality;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Quality
{
    /// <summary>
    /// Walks the existing library through the audio-quality probe, once.
    ///
    /// <para>
    /// New imports are probed from the scan hook; this exists for the books that were
    /// already here, and it re-opens every verdict at once when the probe's version
    /// changes. It enqueues a small batch per tick and only when the previous batch has
    /// drained, so a library of a thousand books fills in over hours without ever putting
    /// a wall of jobs in front of something the operator just asked for.
    /// </para>
    /// </summary>
    public sealed class AudioQualitySweepService(
        IServiceScopeFactory scopeFactory,
        IWorkerCycleRunner cycleRunner,
        ILibraryFilesystemReadiness filesystemReadiness,
        ILogger<AudioQualitySweepService> logger) : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Per tick. A probe runs seconds, so a batch drains well inside one interval;
        /// anything queued by hand in between waits behind at most this many probes.
        /// </summary>
        public const int BatchSize = 25;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await filesystemReadiness.WaitUntilReadyAsync(stoppingToken);
            logger.LogInformation("Audio-quality sweep started");

            await cycleRunner.RunPeriodicAsync(
                "quality-sweep",
                initialDelay: TimeSpan.FromMinutes(2),
                intervalProvider: () => PollInterval,
                runCycle: RunCycleAsync,
                cancellationToken: stoppingToken);
        }

        private async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();

            var settings = await scope.ServiceProvider.GetRequiredService<IConfigurationService>()
                .GetApplicationSettingsAsync();
            if (!settings.AudioQualityProbeEnabled)
            {
                return;
            }

            var repository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var pending = await repository.GetIdsNeedingAudioQualityProbeAsync(
                AudioQualityIdentity.CurrentVersionSuffix,
                BatchSize,
                cancellationToken);
            if (pending.Count == 0)
            {
                return;
            }

            var prober = scope.ServiceProvider.GetRequiredService<IAudioQualityService>();
            var queued = 0;
            foreach (var audiobookId in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await prober.EnqueueAsync(audiobookId, TagTrigger.Automatic, cancellationToken);
                if (result.Queued)
                {
                    queued++;
                }
            }

            if (queued > 0)
            {
                logger.LogInformation("Audio-quality sweep queued {Count} probe(s)", queued);
            }
        }
    }
}
