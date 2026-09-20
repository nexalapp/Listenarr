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
using Listenarr.Application.Audiobooks.Suggestions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Suggestions.Workers
{
    /// <summary>
    /// Fills the Suggested page's catalog cache one name at a time, on the configured
    /// interval. A whole library is hundreds of Audible requests; the refresh button
    /// makes them back to back, which is fine once but not while someone is also
    /// searching. This does the same work at a pace the provider never notices.
    /// </summary>
    public class SuggestionCatalogBackgroundService(
        ILogger<SuggestionCatalogBackgroundService> logger,
        ISuggestionCatalogProcessor processor,
        IWorkerCycleRunner cycleRunner) : BackgroundService
    {
        /// <summary>How often the loop wakes to check whether a fetch is due.</summary>
        private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("SuggestionCatalogBackgroundService started");

            await cycleRunner.RunPeriodicAsync(
                nameof(SuggestionCatalogBackgroundService),
                initialDelay: TimeSpan.FromMinutes(2),
                intervalProvider: () => Tick,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("SuggestionCatalogBackgroundService stopped");
        }
    }

    public interface ISuggestionCatalogProcessor
    {
        Task RunCycleAsync(CancellationToken cancellationToken);
    }

    public class SuggestionCatalogProcessor(
        ILogger<SuggestionCatalogProcessor> logger,
        IServiceScopeFactory serviceScopeFactory,
        ISuggestionRefreshService refresh,
        TimeProvider timeProvider) : ISuggestionCatalogProcessor
    {
        private DateTime? _lastFetchStartedAt;

        /// <summary>
        /// One fetch when the configured interval has elapsed since the last one
        /// started; nothing when the interval is zero, when nothing is missing, or
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
                intervalMinutes = settings.SuggestionsBackgroundFetchIntervalMinutes;
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
                    logger.LogDebug("Suggestion catalog fetched in the background for {Name}", fetched);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
            {
                logger.LogWarning(ex, "Background suggestion catalog fetch failed");
            }
        }
    }
}
