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
using Listenarr.Application.Audiobooks.AutoGrab;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.AutoGrab
{
    /// <summary>
    /// Runs auto-grab on its interval. One run adds at most what the setting allows, so
    /// a list that has filled up overnight arrives over hours rather than in one burst
    /// that fills a disk before anyone sees it.
    /// </summary>
    public class AutoGrabBackgroundService(
        ILogger<AutoGrabBackgroundService> logger,
        IAutoGrabProcessor processor,
        IWorkerCycleRunner cycleRunner) : BackgroundService
    {
        private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("AutoGrabBackgroundService started");

            await cycleRunner.RunPeriodicAsync(
                nameof(AutoGrabBackgroundService),
                initialDelay: TimeSpan.FromMinutes(5),
                intervalProvider: () => Tick,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("AutoGrabBackgroundService stopped");
        }
    }

    public interface IAutoGrabProcessor
    {
        Task RunCycleAsync(CancellationToken cancellationToken);
    }

    public class AutoGrabProcessor(
        ILogger<AutoGrabProcessor> logger,
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider) : IAutoGrabProcessor
    {
        private DateTime? _lastRunStartedAt;

        /// <summary>
        /// One run when the configured interval has elapsed and the switch is on.
        /// Everything is read fresh each cycle, so turning it off takes effect at the
        /// next tick rather than at the next restart.
        /// </summary>
        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceScopeFactory.CreateScope();
            var settings = AutoGrabSettingsJson.Read(
                (await scope.ServiceProvider
                    .GetRequiredService<IConfigurationService>()
                    .GetApplicationSettingsAsync()).AutoGrabSettingsJson);

            if (!settings.Enabled || !settings.Active.Any())
            {
                return;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (_lastRunStartedAt.HasValue
                && now - _lastRunStartedAt.Value < TimeSpan.FromMinutes(settings.IntervalMinutes))
            {
                return;
            }

            _lastRunStartedAt = now;
            try
            {
                var report = await scope.ServiceProvider
                    .GetRequiredService<IAutoGrabService>()
                    .RunAsync(cancellationToken);

                if (report.Added > 0 || report.Failed > 0)
                {
                    logger.LogInformation(
                        "Auto-grab run added {Added} and failed {Failed}", report.Added, report.Failed);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
            {
                logger.LogWarning(ex, "Auto-grab run failed");
            }
        }
    }
}
