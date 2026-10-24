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
using Listenarr.Application.Audiobooks.Discover;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Discover.Workers
{
    /// <summary>
    /// Fills the discovery tabs in the background, one page or one lookup at a time, on
    /// the configured interval. A chart category is five requests and a followed board
    /// is five more; done all at once on every app start that is a burst, done one at a
    /// time it is invisible.
    /// </summary>
    public class DiscoverBackgroundService(
        ILogger<DiscoverBackgroundService> logger,
        IDiscoverFetchProcessor processor,
        IWorkerCycleRunner cycleRunner) : BackgroundService
    {
        /// <summary>How often the loop wakes to check whether a fetch is due.</summary>
        private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("DiscoverBackgroundService started");

            await cycleRunner.RunPeriodicAsync(
                nameof(DiscoverBackgroundService),
                initialDelay: TimeSpan.FromMinutes(3),
                intervalProvider: () => Tick,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("DiscoverBackgroundService stopped");
        }
    }

    public interface IDiscoverFetchProcessor
    {
        Task RunCycleAsync(CancellationToken cancellationToken);
    }

    public class DiscoverFetchProcessor(
        ILogger<DiscoverFetchProcessor> logger,
        IServiceScopeFactory serviceScopeFactory,
        IDiscoverRefreshService refresh,
        TimeProvider timeProvider) : IDiscoverFetchProcessor
    {
        private DateTime? _lastFetchStartedAt;

        /// <summary>
        /// One unit of work when the configured interval has elapsed since the last one
        /// started; nothing when the interval is zero, when no section is followed, or
        /// when a refresh is already running. The interval is read each cycle so a
        /// settings change takes effect without a restart.
        /// </summary>
        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            int intervalMinutes;
            using (var scope = serviceScopeFactory.CreateScope())
            {
                var settings = await scope.ServiceProvider
                    .GetRequiredService<IConfigurationService>()
                    .GetApplicationSettingsAsync();
                intervalMinutes = settings.DiscoverBackgroundFetchIntervalMinutes;
            }

            if (intervalMinutes <= 0)
            {
                return;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (_lastFetchStartedAt.HasValue && now - _lastFetchStartedAt.Value < TimeSpan.FromMinutes(intervalMinutes))
            {
                return;
            }

            _lastFetchStartedAt = now;
            try
            {
                var fetched = await refresh.FetchNextNeededAsync(cancellationToken);
                if (fetched != null)
                {
                    logger.LogDebug("Discovery fetched in the background: {What}", fetched);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
            {
                logger.LogWarning(ex, "Background discovery fetch failed");
            }
        }
    }
}
