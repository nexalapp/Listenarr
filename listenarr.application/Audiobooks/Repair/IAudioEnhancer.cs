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
    public enum EnhancerState
    {
        /// <summary>Nothing installed. Enabling repairs starts the download.</summary>
        NotProvisioned,

        /// <summary>The runtime, packages or model weights are still downloading.</summary>
        Provisioning,

        Ready,

        /// <summary>The last provisioning attempt failed; the error says why and a retry redoes it.</summary>
        Failed
    }

    /// <summary>Where the enhancer stands, for the settings page and for every refusal message.</summary>
    /// <param name="State">The state.</param>
    /// <param name="ProvisionProgress">0–100 while provisioning; meaningless otherwise.</param>
    /// <param name="ProvisionStep">What is being fetched right now, for the settings page's one line.</param>
    /// <param name="PipelineVersion">The installed pipeline's version tag, once Ready.</param>
    /// <param name="DiskBytes">What the install occupies, once Ready.</param>
    /// <param name="Error">Why provisioning failed, when it did.</param>
    public sealed record EnhancerStatus(
        EnhancerState State,
        double ProvisionProgress,
        string? ProvisionStep,
        string? PipelineVersion,
        long? DiskBytes,
        string? Error);

    /// <summary>Thrown by an enhance call made while the enhancer is not Ready.</summary>
    public sealed class EnhancerUnavailableException(string message) : Exception(message);

    /// <summary>
    /// The generative speech-enhancement model, run as a local subprocess against one
    /// bounded chunk of audio at a time.
    ///
    /// <para>
    /// Provisioned lazily into the config volume on first enable — a Python runtime,
    /// pinned packages and the model weights, about four gigabytes once — so the
    /// application image never carries it and an install that never turns repairs on
    /// never pays for it.
    /// </para>
    /// <para>
    /// Chunked by the caller, not here: a chunk is the unit of crash loss and of memory
    /// (the model holds 5–10 GB while working), and the caller owns both budgets. Calls
    /// must be serialized by the caller-side gate — two model processes would double the
    /// memory on a host that cannot spare it.
    /// </para>
    /// </summary>
    public interface IAudioEnhancer
    {
        /// <summary>Ready right now; nothing waits and nothing downloads.</summary>
        Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

        Task<EnhancerStatus> GetStatusAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Start provisioning if it has not happened; returns at once with the current
        /// status. Idempotent — a second call while one runs just reports progress.
        /// </summary>
        Task<EnhancerStatus> ProvisionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// One mono 44.1 kHz WAV chunk in, the enhanced WAV out. Blocks for roughly one
        /// to two times the chunk's duration on eight threads; a hard timeout kills a
        /// stuck model. Throws <see cref="EnhancerUnavailableException"/> when not Ready.
        /// </summary>
        Task EnhanceChunkAsync(
            string inputWavPath,
            string outputWavPath,
            TimeSpan chunkDuration,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The one-at-a-time gate every enhance call passes through. A singleton, because the
    /// constraint is per process: the repair lane is serial on its own, and the gate
    /// exists so a preview asked for mid-repair waits for the current chunk instead of
    /// starting a second five-gigabyte model beside the first.
    /// </summary>
    public sealed class EnhancerGate
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            return new Releaser(_gate);
        }

        private sealed class Releaser(SemaphoreSlim gate) : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    gate.Release();
                }
            }
        }
    }
}
