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

namespace Listenarr.Infrastructure.Search.NzbKing
{
    /// <summary>
    /// Runs the NZBKing keepalive daily. The idle threshold is 28 days, so a daily cadence
    /// leaves several cycles of slack before NZBKing's one-month deletion would bite.
    /// </summary>
    public class NzbKingKeepaliveBackgroundService(
        ILogger<NzbKingKeepaliveBackgroundService> logger,
        INzbKingKeepaliveProcessor processor,
        IWorkerCycleRunner cycleRunner) : BackgroundService
    {
        private static readonly TimeSpan CycleInterval = TimeSpan.FromDays(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation(
                "NzbKingKeepaliveBackgroundService started; idle keys are touched every {Hours} hours",
                CycleInterval.TotalHours);

            await cycleRunner.RunPeriodicAsync(
                nameof(NzbKingKeepaliveBackgroundService),
                initialDelay: TimeSpan.FromMinutes(15),
                intervalProvider: () => CycleInterval,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("NzbKingKeepaliveBackgroundService stopped");
        }
    }
}
