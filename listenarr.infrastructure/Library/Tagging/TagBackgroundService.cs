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

namespace Listenarr.Infrastructure.Library.Tagging
{
    /// <summary>
    /// Drives the tag-writing queue on a timer.
    ///
    /// Waits for the library filesystem the same way the import worker does: a tag write
    /// reads and replaces library files, and starting before the mounts are checked would
    /// fail every job in the queue for a reason that is not their fault — and, worse,
    /// would do so after removing a file.
    /// </summary>
    public sealed class TagBackgroundService(
        ITagJobProcessor processor,
        IWorkerCycleRunner cycleRunner,
        ILibraryFilesystemReadiness filesystemReadiness,
        ILogger<TagBackgroundService> logger) : BackgroundService
    {
        /// <summary>
        /// Long, because jobs are enqueued rather than discovered: a new one is picked up
        /// on the next tick, and nothing is gained by polling harder.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Tag-writing worker waiting for library filesystem initialization");
            await filesystemReadiness.WaitUntilReadyAsync(stoppingToken);
            logger.LogInformation("Tag-writing worker started");

            await cycleRunner.RunPeriodicAsync(
                "tagging",
                initialDelay: TimeSpan.FromSeconds(20),
                intervalProvider: () => PollInterval,
                runCycle: processor.RunCycleAsync,
                cancellationToken: stoppingToken);
        }
    }
}
