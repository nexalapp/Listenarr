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
namespace Listenarr.Application.Audiobooks.Repair
{
    /// <summary>One chunk handed to a worker: what it is called and how long it plays.</summary>
    public sealed record EnhancerWorkerTask(Guid Id, double DurationSeconds);

    /// <summary>What is known about one worker, for the status endpoint and the logs.</summary>
    public sealed record EnhancerWorkerStatus(
        string WorkerId,
        DateTime LastSeenUtc,
        bool Alive,
        bool Ready,
        int Completed,
        int Failed,
        double? LastRealtimeFactor);

    /// <summary>
    /// Hands enhancement chunks to a model running on another machine — the GPU box —
    /// which pulls them over HTTP, the way Vocable's narration worker does.
    ///
    /// <para>
    /// Pull, not push: the worker owns the card and knows when it is free (the box's day
    /// job can take it at any moment), so it asks for work only when it can do it, and
    /// nothing here needs to know its address. A worker that stops asking is gone; its
    /// chunk goes back to whoever asks next, and with nobody asking the local model does
    /// the work as it always did.
    /// </para>
    /// <para>
    /// In memory, deliberately. A chunk is a minute of GPU time, and the repair it belongs
    /// to is already resumable chunk by chunk from its manifest on disk: a restart here
    /// loses the chunk in flight and nothing else, which is exactly what it lost before.
    /// </para>
    /// </summary>
    public interface IEnhancerWorkerBroker
    {
        /// <summary>A worker has asked for work recently, whether or not its card is free.</summary>
        bool HasLiveWorker { get; }

        IReadOnlyList<EnhancerWorkerStatus> Workers { get; }

        /// <summary>
        /// Enhance one chunk on a worker. False when no worker is there to do it, or it
        /// failed the chunk three times; the caller then runs the local model. While a
        /// live worker is merely busy, this waits rather than falling back — the card
        /// frees in minutes and the CPU takes over an hour for the same chunk.
        /// </summary>
        Task<bool> TryEnhanceAsync(
            string inputWavPath,
            string outputWavPath,
            TimeSpan chunkDuration,
            CancellationToken cancellationToken);

        /// <summary>
        /// A worker asking for work. <paramref name="ready"/> false is a heartbeat only:
        /// the worker is there but its card is taken. Waits up to <paramref name="wait"/>
        /// for a chunk so an idle worker is not polling in a tight loop.
        /// </summary>
        Task<EnhancerWorkerTask?> ClaimAsync(
            string workerId,
            bool ready,
            TimeSpan wait,
            CancellationToken cancellationToken);

        /// <summary>The chunk's input, only to the worker holding it; null otherwise.</summary>
        string? InputPathFor(Guid taskId, string workerId);

        /// <summary>The enhanced chunk, streamed back. False when the chunk is no longer this worker's.</summary>
        Task<bool> CompleteAsync(Guid taskId, string workerId, Stream body, CancellationToken cancellationToken);

        /// <summary>The worker could not enhance the chunk. False when it was not this worker's.</summary>
        bool Fail(Guid taskId, string workerId, string reason);
    }
}
