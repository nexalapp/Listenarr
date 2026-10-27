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

namespace Listenarr.Infrastructure.HostedServices
{
    public sealed class WorkerCycleRunner(
        TimeProvider timeProvider,
        IAppMetricsService metrics,
        IWorkerScheduleRegistry schedules,
        ILogger<WorkerCycleRunner> logger) : IWorkerCycleRunner
    {
        public async Task RunPeriodicAsync(
            string workerName,
            TimeSpan? initialDelay,
            Func<TimeSpan> intervalProvider,
            Func<CancellationToken, Task> runCycle,
            CancellationToken cancellationToken)
        {
            // Register before the delay, not after the first cycle. A worker sitting out
            // a staggered startup delay is exactly the one someone is looking for when
            // they ask why nothing has happened, and it used to be absent from the list
            // entirely until its first cycle began.
            schedules.Waiting(
                workerName,
                intervalProvider(),
                (timeProvider.GetUtcNow() + (initialDelay ?? TimeSpan.Zero)).UtcDateTime);

            if (initialDelay is { } delay && delay > TimeSpan.Zero)
            {
                // Waking works during the startup delay too; that is the wait people hit.
                using var wokenEarly = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, schedules.WakeToken(workerName));
                try
                {
                    await Task.Delay(delay, timeProvider, wokenEarly.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation(
                        "{WorkerName} woken early by request, before its first cycle", workerName);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    metrics.Increment(BuildMetricName(workerName, "cycle.skipped"));
                    logger.LogInformation("{WorkerName} canceled before first cycle", workerName);
                    return;
                }
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                using var scope = logger.BeginScope(new Dictionary<string, object?>
                {
                    ["WorkerName"] = workerName,
                    ["WorkerCycleId"] = Guid.NewGuid().ToString("N")
                });

                var startedAt = timeProvider.GetUtcNow();
                var outcome = WorkerCycleOutcome.Completed;
                var stopping = false;
                schedules.CycleStarting(workerName, intervalProvider());
                try
                {
                    metrics.Increment(BuildMetricName(workerName, "cycle.started"));
                    await runCycle(cancellationToken);
                    metrics.Increment(BuildMetricName(workerName, "cycle.completed"));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    metrics.Increment(BuildMetricName(workerName, "cycle.skipped"));
                    outcome = WorkerCycleOutcome.Stopped;
                    stopping = true;
                }
                catch (OperationCanceledException ex)
                {
                    metrics.Increment(BuildMetricName(workerName, "cycle.failed"));
                    outcome = WorkerCycleOutcome.Failed;
                    logger.LogWarning(ex, "{WorkerName} cycle canceled/timed out; continuing", workerName);
                }
                catch (Exception ex) when (Listenarr.Application.Common.WorkerExceptionClassifier.IsNonFatal(ex))
                {
                    metrics.Increment(BuildMetricName(workerName, "cycle.failed"));
                    outcome = WorkerCycleOutcome.Failed;
                    logger.LogError(ex, "Error in {WorkerName} cycle", workerName);
                }

                schedules.CycleFinished(
                    workerName, timeProvider.GetUtcNow() - startedAt, outcome);

                if (stopping)
                {
                    break;
                }

                var interval = intervalProvider();
                schedules.Waiting(
                    workerName, interval, (timeProvider.GetUtcNow() + interval).UtcDateTime);

                // The wait is interruptible so the tasks page can say "run now" and mean
                // it. Waking is not the same as overruling: a worker that keeps an
                // interval of its own will wake, consult it and go back to sleep.
                using var woken = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, schedules.WakeToken(workerName));
                try
                {
                    await Task.Delay(interval, timeProvider, woken.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("{WorkerName} woken early by request", workerName);
                }
            }
        }

        private static string BuildMetricName(string workerName, string suffix)
        {
            var normalized = new string(workerName
                .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '.')
                .ToArray());

            return $"worker.{normalized}.{suffix}";
        }
    }
}
