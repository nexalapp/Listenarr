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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Repair
{
    /// <summary>
    /// Drives the repair lane of the conversion queue on its own timer. Its own worker,
    /// not conversion's: a repair runs the enhancement model for a day or two, and a
    /// single serial loop would park every ordinary conversion behind it. The lane is
    /// still serial within itself — the model holds five to ten gigabytes while it works,
    /// and two at once is a machine that falls over.
    /// </summary>
    public sealed class RepairBackgroundService(
        IConversionJobProcessor processor,
        IWorkerCycleRunner cycleRunner,
        ILibraryFilesystemReadiness filesystemReadiness,
        ILogger<RepairBackgroundService> logger) : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Repair worker waiting for library filesystem initialization");
            await filesystemReadiness.WaitUntilReadyAsync(stoppingToken);
            logger.LogInformation("Repair worker started");

            await cycleRunner.RunPeriodicAsync(
                "repair",
                initialDelay: TimeSpan.FromSeconds(25),
                intervalProvider: () => PollInterval,
                runCycle: processor.RunRepairCycleAsync,
                cancellationToken: stoppingToken);
        }
    }
}
