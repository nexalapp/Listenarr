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
using System.Collections.Concurrent;
using Listenarr.Application.Common.Contracts;

namespace Listenarr.Infrastructure.HostedServices
{
    /// <summary>
    /// Holds what each worker is doing, for the tasks page. In memory on purpose: it
    /// describes this process's loops, and a loop does not outlive the process.
    /// </summary>
    public sealed class WorkerScheduleRegistry(TimeProvider time) : IWorkerScheduleRegistry
    {
        private sealed class Entry
        {
            public TimeSpan Interval;
            public DateTime? LastStartedAt;
            public TimeSpan? LastDuration;
            public WorkerCycleOutcome LastOutcome = WorkerCycleOutcome.NotYetRun;
            public DateTime? NextDueAt;
            public bool Running;
            public CancellationTokenSource Wake = new();
        }

        private readonly ConcurrentDictionary<string, Entry> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<WorkerSchedule> Snapshot() =>
            [.. _entries
                .Select(pair => new WorkerSchedule(
                    pair.Key,
                    pair.Value.Interval,
                    pair.Value.LastStartedAt,
                    pair.Value.LastDuration,
                    pair.Value.LastOutcome,
                    pair.Value.Running ? null : pair.Value.NextDueAt,
                    pair.Value.Running))
                .OrderBy(schedule => schedule.Name, StringComparer.OrdinalIgnoreCase)];

        public void CycleStarting(string workerName, TimeSpan interval)
        {
            var entry = _entries.GetOrAdd(workerName, _ => new Entry());
            entry.Interval = interval;
            entry.LastStartedAt = time.GetUtcNow().UtcDateTime;
            entry.Running = true;
            entry.NextDueAt = null;
        }

        public void CycleFinished(string workerName, TimeSpan duration, WorkerCycleOutcome outcome)
        {
            var entry = _entries.GetOrAdd(workerName, _ => new Entry());
            entry.LastDuration = duration;
            entry.LastOutcome = outcome;
            entry.Running = false;
        }

        public void Waiting(string workerName, TimeSpan interval, DateTime nextDueAt)
        {
            var entry = _entries.GetOrAdd(workerName, _ => new Entry());
            entry.Interval = interval;
            entry.NextDueAt = nextDueAt;
            entry.Running = false;
        }

        public bool Wake(string workerName)
        {
            if (!_entries.TryGetValue(workerName, out var entry))
            {
                return false;
            }

            // Cancelling the token the runner is waiting on is the whole mechanism; the
            // runner replaces it before the next wait.
            try
            {
                entry.Wake.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Raced with the runner swapping it; the next wait is the fresh one.
            }

            return true;
        }

        public CancellationToken WakeToken(string workerName)
        {
            var entry = _entries.GetOrAdd(workerName, _ => new Entry());

            // A used source stays cancelled, so each wait gets its own.
            if (entry.Wake.IsCancellationRequested)
            {
                var spent = entry.Wake;
                entry.Wake = new CancellationTokenSource();
                spent.Dispose();
            }

            return entry.Wake.Token;
        }
    }
}
