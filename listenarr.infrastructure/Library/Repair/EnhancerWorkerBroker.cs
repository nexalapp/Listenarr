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
using Listenarr.Application.Audiobooks.Repair;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Repair
{
    /// <summary>
    /// The in-memory hand-off between the repair lane and a remote GPU worker. See
    /// <see cref="IEnhancerWorkerBroker"/> for why it pulls and why nothing is persisted.
    /// </summary>
    public sealed class EnhancerWorkerBroker(
        ILogger<EnhancerWorkerBroker> logger,
        TimeProvider? timeProvider = null) : IEnhancerWorkerBroker
    {
        /// <summary>
        /// A worker long-polls for twenty seconds at a time, so one unheard from for
        /// ninety has stopped, not paused.
        /// </summary>
        internal static readonly TimeSpan AliveWindow = TimeSpan.FromSeconds(90);

        /// <summary>
        /// How many times a chunk is offered to workers before the local model takes it.
        /// One failure is a card taken mid-chunk; three in a row is the chunk or the worker.
        /// </summary>
        internal const int RemoteAttempts = 3;

        /// <summary>An enhanced 20-minute chunk is ~210 MB of float WAV; anything past this is not one.</summary>
        internal const long MaxUploadBytes = 2L * 1024 * 1024 * 1024;

        internal TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
        private readonly object _lock = new();
        private readonly Dictionary<string, WorkerState> _workers = new(StringComparer.Ordinal);
        private readonly List<RemoteChunk> _chunks = [];

        private sealed class WorkerState
        {
            public DateTime LastSeenUtc { get; set; }
            public bool Ready { get; set; }
            public int Completed { get; set; }
            public int Failed { get; set; }
            public double? LastRealtimeFactor { get; set; }
        }

        private sealed class RemoteChunk(Guid id, string input, string output, TimeSpan duration)
        {
            public Guid Id { get; } = id;
            public string Input { get; } = input;
            public string Output { get; } = output;
            public TimeSpan Duration { get; } = duration;
            public string? ClaimedBy { get; set; }
            public DateTime ClaimedAtUtc { get; set; }
            public TaskCompletionSource<bool> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        public bool HasLiveWorker
        {
            get
            {
                lock (_lock)
                {
                    return AnyAlive();
                }
            }
        }

        public IReadOnlyList<EnhancerWorkerStatus> Workers
        {
            get
            {
                lock (_lock)
                {
                    var now = Now;
                    return _workers
                        .Select(pair => new EnhancerWorkerStatus(
                            pair.Key,
                            pair.Value.LastSeenUtc,
                            now - pair.Value.LastSeenUtc <= AliveWindow,
                            pair.Value.Ready,
                            pair.Value.Completed,
                            pair.Value.Failed,
                            pair.Value.LastRealtimeFactor))
                        .ToList();
                }
            }
        }

        private bool AnyAlive()
        {
            var now = Now;
            return _workers.Values.Any(worker => now - worker.LastSeenUtc <= AliveWindow);
        }

        /// <summary>
        /// How long a claimed chunk may take before it is presumed lost: as long as it
        /// plays, plus slack for the transfer. The GPU runs it twenty times faster than
        /// that, so only a worker that died with the chunk ever reaches it.
        /// </summary>
        internal static TimeSpan ClaimDeadline(TimeSpan duration) => duration + TimeSpan.FromMinutes(5);

        public async Task<bool> TryEnhanceAsync(
            string inputWavPath,
            string outputWavPath,
            TimeSpan chunkDuration,
            CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= RemoteAttempts; attempt++)
            {
                RemoteChunk chunk;
                lock (_lock)
                {
                    if (!AnyAlive())
                    {
                        if (attempt > 1)
                        {
                            logger.LogWarning(
                                "The GPU worker went away mid-chunk; the local model takes {Chunk}",
                                Path.GetFileName(inputWavPath));
                        }

                        return false;
                    }

                    chunk = new RemoteChunk(Guid.NewGuid(), inputWavPath, outputWavPath, chunkDuration);
                    _chunks.Add(chunk);
                }

                try
                {
                    var outcome = await AwaitChunkAsync(chunk, cancellationToken);
                    if (outcome == ChunkWait.Done)
                    {
                        return true;
                    }

                    if (outcome == ChunkWait.NobodyLeft)
                    {
                        logger.LogWarning(
                            "No GPU worker is asking for work any more; the local model takes {Chunk}",
                            Path.GetFileName(inputWavPath));
                        return false;
                    }
                }
                finally
                {
                    lock (_lock)
                    {
                        _chunks.Remove(chunk);
                    }
                }

                logger.LogWarning(
                    "The GPU worker did not return {Chunk} (attempt {Attempt} of {Attempts})",
                    Path.GetFileName(inputWavPath),
                    attempt,
                    RemoteAttempts);
            }

            logger.LogWarning(
                "The GPU worker failed {Chunk} {Attempts} times; the local model takes it",
                Path.GetFileName(inputWavPath),
                RemoteAttempts);
            return false;
        }

        private enum ChunkWait
        {
            Done,
            Failed,
            NobodyLeft
        }

        private async Task<ChunkWait> AwaitChunkAsync(RemoteChunk chunk, CancellationToken cancellationToken)
        {
            while (true)
            {
                var finished = await Task.WhenAny(chunk.Result.Task, Task.Delay(PollInterval, _time, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (finished == chunk.Result.Task)
                {
                    return await chunk.Result.Task ? ChunkWait.Done : ChunkWait.Failed;
                }

                lock (_lock)
                {
                    if (chunk.Result.Task.IsCompleted)
                    {
                        continue;
                    }

                    if (chunk.ClaimedBy == null)
                    {
                        // Waiting for a worker whose card is taken is right; waiting for one
                        // that has gone is not.
                        if (!AnyAlive())
                        {
                            chunk.Result.TrySetResult(false);
                            return ChunkWait.NobodyLeft;
                        }

                        continue;
                    }

                    if (Now - chunk.ClaimedAtUtc > ClaimDeadline(chunk.Duration))
                    {
                        if (_workers.TryGetValue(chunk.ClaimedBy, out var worker))
                        {
                            worker.Failed++;
                        }

                        chunk.Result.TrySetResult(false);
                        return ChunkWait.Failed;
                    }
                }
            }
        }

        public async Task<EnhancerWorkerTask?> ClaimAsync(
            string workerId,
            bool ready,
            TimeSpan wait,
            CancellationToken cancellationToken)
        {
            var until = Now + wait;
            while (true)
            {
                lock (_lock)
                {
                    if (!_workers.TryGetValue(workerId, out var worker))
                    {
                        worker = new WorkerState();
                        _workers[workerId] = worker;
                        logger.LogInformation("GPU worker {Worker} connected", workerId);
                    }

                    worker.LastSeenUtc = Now;
                    worker.Ready = ready;
                    // A worker takes one chunk at a time, so one asking again while it still
                    // holds a chunk has lost it -- killed and restarted mid-chunk. Waiting out
                    // the deadline would hold the enhancer, and every lane behind it, for 25 min.
                    foreach (var lost in _chunks.Where(chunk =>
                        string.Equals(chunk.ClaimedBy, workerId, StringComparison.Ordinal) && !chunk.Result.Task.IsCompleted))
                    {
                        worker.Failed++;
                        logger.LogWarning("GPU worker {Worker} came back without returning {Chunk}", workerId, Path.GetFileName(lost.Input));
                        lost.Result.TrySetResult(false);
                    }

                    if (!ready)
                    {
                        return null;
                    }

                    var next = _chunks.FirstOrDefault(chunk => chunk.ClaimedBy == null && !chunk.Result.Task.IsCompleted);
                    if (next != null)
                    {
                        next.ClaimedBy = workerId;
                        next.ClaimedAtUtc = Now;
                        return new EnhancerWorkerTask(next.Id, next.Duration.TotalSeconds);
                    }
                }

                if (Now >= until)
                {
                    return null;
                }

                await Task.Delay(PollInterval, _time, cancellationToken);
            }
        }

        public string? InputPathFor(Guid taskId, string workerId)
        {
            lock (_lock)
            {
                return Held(taskId, workerId)?.Input is { } input && File.Exists(input) ? input : null;
            }
        }

        private RemoteChunk? Held(Guid taskId, string workerId) =>
            _chunks.FirstOrDefault(chunk =>
                chunk.Id == taskId
                && string.Equals(chunk.ClaimedBy, workerId, StringComparison.Ordinal)
                && !chunk.Result.Task.IsCompleted);

        public async Task<bool> CompleteAsync(Guid taskId, string workerId, Stream body, CancellationToken cancellationToken)
        {
            string output;
            lock (_lock)
            {
                var chunk = Held(taskId, workerId);
                if (chunk == null)
                {
                    return false;
                }

                output = chunk.Output;
            }

            // Written beside the destination and moved in only while the chunk is still
            // this worker's: a late upload after the lane gave up must never land on a
            // file the local model is writing.
            var upload = output + $".{taskId:N}.upload";
            try
            {
                long written;
                await using (var file = new FileStream(upload, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    written = await CopyBoundedAsync(body, file, MaxUploadBytes, cancellationToken);
                }

                if (written < 44 || !StartsAsWave(upload))
                {
                    logger.LogWarning("GPU worker {Worker} returned something that is not a WAV ({Bytes} bytes)", workerId, written);
                    Fail(taskId, workerId, "The upload was not a WAV file.");
                    return false;
                }

                lock (_lock)
                {
                    var chunk = Held(taskId, workerId);
                    if (chunk == null)
                    {
                        return false;
                    }

                    File.Move(upload, output, overwrite: true);
                    var worker = _workers[workerId];
                    worker.Completed++;
                    worker.LastSeenUtc = Now;
                    var seconds = (Now - chunk.ClaimedAtUtc).TotalSeconds;
                    worker.LastRealtimeFactor = seconds > 0 ? chunk.Duration.TotalSeconds / seconds : null;
                    logger.LogInformation(
                        "GPU worker {Worker} enhanced {Chunk} ({Duration}) in {Seconds:0}s, {Factor:0.0}x realtime",
                        workerId,
                        Path.GetFileName(chunk.Input),
                        chunk.Duration,
                        seconds,
                        worker.LastRealtimeFactor);
                    chunk.Result.TrySetResult(true);
                    return true;
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException)
            {
                // A dropped connection or an oversized body: the chunk goes back to be
                // offered again now, not when its deadline lapses.
                Fail(taskId, workerId, $"The upload did not complete: {exception.Message}");
                return false;
            }
            finally
            {
                try
                {
                    File.Delete(upload);
                }
                catch (IOException)
                {
                }
            }
        }

        private static async Task<long> CopyBoundedAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
        {
            var buffer = new byte[1 << 16];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > limit)
                {
                    throw new InvalidDataException($"The upload exceeds {limit:N0} bytes.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return total;
        }

        private static bool StartsAsWave(string path)
        {
            Span<byte> header = stackalloc byte[12];
            using var file = File.OpenRead(path);
            return file.Read(header) == 12
                   && header[..4].SequenceEqual("RIFF"u8)
                   && header[8..12].SequenceEqual("WAVE"u8);
        }

        public bool Fail(Guid taskId, string workerId, string reason)
        {
            lock (_lock)
            {
                var chunk = Held(taskId, workerId);
                if (chunk == null)
                {
                    return false;
                }

                if (_workers.TryGetValue(workerId, out var worker))
                {
                    worker.Failed++;
                    worker.LastSeenUtc = Now;
                }

                logger.LogWarning(
                    "GPU worker {Worker} could not enhance {Chunk}: {Reason}",
                    workerId,
                    Path.GetFileName(chunk.Input),
                    // Remote text going into the log: one line, bounded.
                    (reason.Length <= 300 ? reason : reason[..300]).ReplaceLineEndings(" "));
                chunk.Result.TrySetResult(false);
                return true;
            }
        }
    }
}
