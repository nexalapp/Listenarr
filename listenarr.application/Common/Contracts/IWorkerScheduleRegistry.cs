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
namespace Listenarr.Application.Common.Contracts
{
    /// <summary>How a worker's last cycle ended.</summary>
    public enum WorkerCycleOutcome
    {
        /// <summary>It has not run since the process started.</summary>
        NotYetRun,
        Completed,
        Failed,

        /// <summary>Shutdown cut it short; not a fault.</summary>
        Stopped
    }

    /// <summary>What the tasks page shows for one worker.</summary>
    /// <param name="Name">The worker's class name, as it registered itself.</param>
    /// <param name="Interval">How long it waits between cycles, as of the last wait.</param>
    /// <param name="LastStartedAt">When its last cycle began, null if none has.</param>
    /// <param name="LastDuration">How long that cycle took.</param>
    /// <param name="LastOutcome">How it ended.</param>
    /// <param name="NextDueAt">When the next cycle is expected; null while one is running.</param>
    /// <param name="Running">True while a cycle is in progress.</param>
    public sealed record WorkerSchedule(
        string Name,
        TimeSpan Interval,
        DateTime? LastStartedAt,
        TimeSpan? LastDuration,
        WorkerCycleOutcome LastOutcome,
        DateTime? NextDueAt,
        bool Running);

    /// <summary>
    /// What every background worker is doing and when it will next do it.
    ///
    /// Every worker goes through <see cref="IWorkerCycleRunner"/>, which already knows
    /// each one's name, its interval and where its cycles begin and end, so this is that
    /// knowledge written down rather than anything new. It exists because "nothing is
    /// happening" and "it is waiting out a delay nobody can see" looked identical from
    /// the outside, repeatedly.
    ///
    /// State is per-process: a restart empties it, and the page says so rather than
    /// pretending a worker has never run.
    /// </summary>
    public interface IWorkerScheduleRegistry
    {
        /// <summary>Every worker that has registered itself this run, by name.</summary>
        IReadOnlyList<WorkerSchedule> Snapshot();

        void CycleStarting(string workerName, TimeSpan interval);

        void CycleFinished(string workerName, TimeSpan duration, WorkerCycleOutcome outcome);

        /// <summary>Records that the worker is now waiting, and until when.</summary>
        void Waiting(string workerName, TimeSpan interval, DateTime nextDueAt);

        /// <summary>
        /// Cuts short the wait of a worker that is sleeping, so its next cycle starts
        /// now. Returns false when no worker of that name has registered.
        ///
        /// It wakes the loop; it does not overrule a worker that keeps an interval of
        /// its own - auto-grab will wake, look at its own clock and go back to sleep. The
        /// page says as much, and such workers carry their own run-now endpoint.
        /// </summary>
        bool Wake(string workerName);

        /// <summary>
        /// A token that is cancelled when someone asks this worker to wake. The runner
        /// takes a fresh one for each wait.
        /// </summary>
        CancellationToken WakeToken(string workerName);
    }
}
