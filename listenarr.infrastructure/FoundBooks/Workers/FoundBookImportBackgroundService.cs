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
using Listenarr.Application.FoundBooks.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.FoundBooks.Workers
{
    /// <summary>
    /// Runs the imports a person queued from the Found tab, one at a time, in the
    /// order they were queued. Wakes when one is queued, when a retry falls due, and
    /// once a minute regardless — so a wake lost to a restart costs at most a minute,
    /// and a request queued before a restart is picked up on the first pass.
    /// </summary>
    public sealed class FoundBookImportBackgroundService(
        ILogger<FoundBookImportBackgroundService> logger,
        IFoundBookImportSignal signal,
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider) : BackgroundService
    {
        private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("FoundBookImportBackgroundService started");
            var wait = TimeSpan.Zero;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (wait > TimeSpan.Zero)
                    {
                        await signal.WaitAsync(wait, stoppingToken);
                    }

                    var drain = await RunDueAsync(stoppingToken);
                    wait = drain.NextDueAt is { } due
                        ? Clamp(due - timeProvider.GetUtcNow().UtcDateTime)
                        : Tick;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
                {
                    logger.LogError(ex, "Found-book import pass failed");
                    wait = Tick;
                }
            }

            logger.LogInformation("FoundBookImportBackgroundService stopped");
        }

        private async Task<FoundBookImportDrain> RunDueAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceScopeFactory.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<IFoundBookImportService>()
                .RunDueAsync(cancellationToken);
        }

        private static TimeSpan Clamp(TimeSpan until) =>
            until <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : until > Tick ? Tick : until;
    }
}
