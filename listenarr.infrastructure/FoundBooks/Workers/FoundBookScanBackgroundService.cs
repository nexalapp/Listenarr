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
using Listenarr.Application.FoundBooks.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.FoundBooks.Workers
{
    /// <summary>
    /// Scans the watch folders on the configured interval. The interval is read each
    /// cycle so a settings change takes effect without a restart; zero means the
    /// periodic scan is off and only the Found tab's button runs one.
    /// </summary>
    public class FoundBookScanBackgroundService(
        ILogger<FoundBookScanBackgroundService> logger,
        IFoundBookScanProcessor processor,
        IWorkerCycleRunner cycleRunner) : BackgroundService
    {
        /// <summary>How often the loop wakes to check whether a scan is due.</summary>
        private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("FoundBookScanBackgroundService started");

            await cycleRunner.RunPeriodicAsync(
                nameof(FoundBookScanBackgroundService),
                initialDelay: TimeSpan.FromMinutes(5),
                intervalProvider: () => Tick,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("FoundBookScanBackgroundService stopped");
        }
    }

    public class FoundBookScanProcessor(
        ILogger<FoundBookScanProcessor> logger,
        IServiceScopeFactory serviceScopeFactory,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider) : IFoundBookScanProcessor
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTime? _lastScanCompletedAt;
        private DateTime? _lastScanStartedAt;

        public bool IsScanning => _gate.CurrentCount == 0;
        public DateTime? LastScanCompletedAt => _lastScanCompletedAt;

        /// <summary>
        /// The periodic cycle: runs a scan when the configured interval has elapsed
        /// since the last one started. Called every minute; most calls do nothing.
        /// </summary>
        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            int intervalMinutes;
            using (var scope = serviceScopeFactory.CreateScope())
            {
                var settings = await scope.ServiceProvider
                    .GetRequiredService<IConfigurationService>()
                    .GetApplicationSettingsAsync();
                intervalMinutes = settings.FoundBooksScanIntervalMinutes;
            }

            if (intervalMinutes <= 0)
            {
                return;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (_lastScanStartedAt.HasValue && now - _lastScanStartedAt.Value < TimeSpan.FromMinutes(intervalMinutes))
            {
                return;
            }

            await ScanAsync(cancellationToken);
        }

        public bool TriggerScan()
        {
            // Take the gate here, not on the background task, so two requests in the
            // same instant cannot both be told a scan was started.
            if (!_gate.Wait(0))
            {
                return false;
            }

            // Host cancellation rather than the request's: the scan outlives the HTTP
            // call that asked for it, and a closed browser tab should not abandon it.
            _ = Task.Run(() => ScanHoldingGateAsync(applicationLifetime.ApplicationStopping), CancellationToken.None);
            return true;
        }

        private async Task ScanAsync(CancellationToken cancellationToken)
        {
            if (!await _gate.WaitAsync(0, cancellationToken))
            {
                return;
            }

            await ScanHoldingGateAsync(cancellationToken);
        }

        private async Task ScanHoldingGateAsync(CancellationToken cancellationToken)
        {
            try
            {
                _lastScanStartedAt = timeProvider.GetUtcNow().UtcDateTime;
                using var scope = serviceScopeFactory.CreateScope();
                var summary = await scope.ServiceProvider
                    .GetRequiredService<IFoundBookScanService>()
                    .ScanAllAsync(cancellationToken);
                _lastScanCompletedAt = timeProvider.GetUtcNow().UtcDateTime;
                logger.LogInformation(
                    "Found-book scan finished: {Folders} folder(s), {Pending} pending, {Blocked} blocked, {Warnings} warning(s)",
                    summary.WatchFolders.Count,
                    summary.Pending,
                    summary.Blocked,
                    summary.Warnings.Count);

                if (summary.Pending > 0)
                {
                    var identified = await scope.ServiceProvider
                        .GetRequiredService<IFoundBookAutoMatchService>()
                        .RunAsync(cancellationToken);
                    if (identified.Considered > 0)
                    {
                        logger.LogInformation(
                            "Found-book automatic match: {Matched} matched, {Listened} listened to, of {Considered}",
                            identified.Matched,
                            identified.Listened,
                            identified.Considered);
                    }

                    var auto = await scope.ServiceProvider
                        .GetRequiredService<IFoundBookAutoAddService>()
                        .RunAsync(cancellationToken);
                    if (auto.Considered > 0)
                    {
                        logger.LogInformation(
                            "Found-book automatic add: {Added} added, {Skipped} left for review of {Considered}",
                            auto.Added,
                            auto.Skipped,
                            auto.Considered);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Found-book scan cancelled");
            }
            catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
            {
                logger.LogError(ex, "Found-book scan failed");
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
