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
using Listenarr.Application.Common.Contracts;
using Listenarr.Infrastructure.HostedServices;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Tests.Features.Api.Services
{
    /// <summary>
    /// What the tasks page reads, and the one thing it can do.
    ///
    /// <para>
    /// The page exists because a worker that is waiting and a worker that is broken
    /// looked identical from outside the container. So these pin that a cycle's start,
    /// duration and outcome are recorded, that the next due time is only offered while
    /// nothing is running, and that asking a sleeping worker to run cuts its wait short
    /// rather than waiting for the interval to expire.
    /// </para>
    /// </summary>
    [Trait("Name", "WorkerScheduleRegistryTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class WorkerScheduleRegistryTests : BaseTests
    {
        [Fact]
        public void ACycleThatHasRunReportsItsDurationAndOutcome()
        {
            var registry = new WorkerScheduleRegistry(TimeProvider.System);

            registry.CycleStarting("ExampleWorker", TimeSpan.FromMinutes(5));
            registry.CycleFinished(
                "ExampleWorker", TimeSpan.FromSeconds(3), WorkerCycleOutcome.Completed);
            registry.Waiting("ExampleWorker", TimeSpan.FromMinutes(5), new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc));

            var task = Assert.Single(registry.Snapshot());
            Assert.Equal("ExampleWorker", task.Name);
            Assert.Equal(TimeSpan.FromMinutes(5), task.Interval);
            Assert.Equal(TimeSpan.FromSeconds(3), task.LastDuration);
            Assert.Equal(WorkerCycleOutcome.Completed, task.LastOutcome);
            Assert.False(task.Running);
            Assert.NotNull(task.NextDueAt);
        }

        [Fact]
        public void AWorkerThatIsRunningOffersNoNextDueTime()
        {
            var registry = new WorkerScheduleRegistry(TimeProvider.System);
            registry.Waiting("ExampleWorker", TimeSpan.FromMinutes(5), DateTime.UtcNow.AddMinutes(5));

            registry.CycleStarting("ExampleWorker", TimeSpan.FromMinutes(5));

            var task = Assert.Single(registry.Snapshot());
            Assert.True(task.Running);
            Assert.Null(task.NextDueAt);
        }

        [Fact]
        public void AWorkerThatHasNeverRunSaysSoRatherThanLookingIdle()
        {
            var registry = new WorkerScheduleRegistry(TimeProvider.System);
            registry.Waiting("ExampleWorker", TimeSpan.FromMinutes(5), DateTime.UtcNow.AddMinutes(5));

            var task = Assert.Single(registry.Snapshot());
            Assert.Equal(WorkerCycleOutcome.NotYetRun, task.LastOutcome);
            Assert.Null(task.LastStartedAt);
        }

        [Fact]
        public void WakingAWorkerNobodyRegisteredIsNotFound()
        {
            var registry = new WorkerScheduleRegistry(TimeProvider.System);

            Assert.False(registry.Wake("NeverHeardOfIt"));
        }

        [Fact]
        public void EachWaitGetsATokenOfItsOwn()
        {
            var registry = new WorkerScheduleRegistry(TimeProvider.System);
            var first = registry.WakeToken("ExampleWorker");

            Assert.True(registry.Wake("ExampleWorker"));
            Assert.True(first.IsCancellationRequested);

            // A spent source would cancel the next wait the moment it began.
            var second = registry.WakeToken("ExampleWorker");
            Assert.False(second.IsCancellationRequested);
        }

        [Fact]
        public async Task AskingASleepingWorkerToRunDoesNotWaitOutTheInterval()
        {
            var registry = new WorkerScheduleRegistry(TimeProvider.System);
            var runner = new WorkerCycleRunner(
                TimeProvider.System,
                Mock.Of<IAppMetricsService>(),
                registry,
                Mock.Of<ILogger<WorkerCycleRunner>>());

            using var cts = new CancellationTokenSource();
            var cycles = 0;
            var firstCycleDone = new TaskCompletionSource();

            var loop = runner.RunPeriodicAsync(
                "ExampleWorker",
                initialDelay: null,
                // An hour: if waking did not work this test would hang, not fail slowly.
                intervalProvider: () => TimeSpan.FromHours(1),
                runCycle: _ =>
                {
                    if (Interlocked.Increment(ref cycles) == 1)
                    {
                        firstCycleDone.TrySetResult();
                    }
                    else
                    {
                        cts.Cancel();
                    }

                    return Task.CompletedTask;
                },
                cts.Token);

            await firstCycleDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The loop reaches its wait a moment after the cycle returns.
            while (registry.Snapshot().FirstOrDefault()?.NextDueAt is null)
            {
                await Task.Delay(10);
            }

            Assert.True(registry.Wake("ExampleWorker"));
            await loop.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, cycles);
        }
    }
}
