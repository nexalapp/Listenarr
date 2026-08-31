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

namespace Listenarr.Infrastructure.Library.Conversion
{
    /// <summary>
    /// Drives the conversion queue on a timer.
    ///
    /// Waits for the library filesystem the same way the import worker does: a
    /// conversion reads and writes the library, and starting before the mounts are
    /// checked would fail every job in the queue for a reason that is not their fault.
    /// </summary>
    public sealed class ConversionBackgroundService(
        IConversionJobProcessor processor,
        IWorkerCycleRunner cycleRunner,
        ILibraryFilesystemReadiness filesystemReadiness,
        ILogger<ConversionBackgroundService> logger) : BackgroundService
    {
        /// <summary>
        /// Long, because conversions are enqueued rather than discovered: a new job is
        /// picked up on the next tick, and nothing is gained by polling harder.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Conversion worker waiting for library filesystem initialization");
            await filesystemReadiness.WaitUntilReadyAsync(stoppingToken);
            logger.LogInformation("Conversion worker started");

            await cycleRunner.RunPeriodicAsync(
                "conversion",
                initialDelay: TimeSpan.FromSeconds(15),
                intervalProvider: () => PollInterval,
                runCycle: processor.RunCycleAsync,
                cancellationToken: stoppingToken);
        }
    }
}
