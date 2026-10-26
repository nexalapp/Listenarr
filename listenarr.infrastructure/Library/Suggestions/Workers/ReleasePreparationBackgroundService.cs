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
    /// Turns found releases into ready ones, one at a time.
    ///
    /// Paced for the same reason the check is: preparing a post means thanking it and
    /// reading it, which is traffic on somebody's forum and a public action on the
    /// operator's account. One a cycle fills the backlog over hours and nobody notices.
    /// </summary>
    public class ReleasePreparationBackgroundService(
        ILogger<ReleasePreparationBackgroundService> logger,
        IReleasePreparationProcessor processor,
        IWorkerCycleRunner cycleRunner) : BackgroundService
    {
        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(45);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("ReleasePreparationBackgroundService started");

            await cycleRunner.RunPeriodicAsync(
                nameof(ReleasePreparationBackgroundService),
                initialDelay: TimeSpan.FromMinutes(4),
                intervalProvider: () => Tick,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("ReleasePreparationBackgroundService stopped");
        }
    }

    public interface IReleasePreparationProcessor
    {
        Task RunCycleAsync(CancellationToken cancellationToken);
    }

    public class ReleasePreparationProcessor(
        ILogger<ReleasePreparationProcessor> logger,
        IServiceScopeFactory serviceScopeFactory) : IReleasePreparationProcessor
    {
        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceScopeFactory.CreateScope();

            try
            {
                var prepared = await scope.ServiceProvider
                    .GetRequiredService<IReleasePreparationService>()
                    .PrepareNextAsync(cancellationToken);

                if (prepared != null)
                {
                    logger.LogDebug("Prepared a release in the background: {What}", prepared);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
            {
                logger.LogWarning(ex, "Background release preparation failed");
            }
        }
    }
}
