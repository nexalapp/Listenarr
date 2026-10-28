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

namespace Listenarr.Infrastructure.Library.Repair
{
    /// <summary>
    /// The enhancer every caller sees: a remote GPU worker when one is asking for work,
    /// the local model otherwise.
    ///
    /// <para>
    /// Measured on the RTX 5080: a 20-minute chunk in 60 seconds, against 72 minutes on the
    /// NAS's contended CPU, with the spectrum within 0.1 dB below 8 kHz of the CPU's
    /// output. Same model, same weights, same solver settings, so the pipeline version is
    /// shared and one book may mix chunks from both — the difference is the run-to-run
    /// noise a generative model has on either device.
    /// </para>
    /// </summary>
    public sealed class GpuFirstAudioEnhancer(
        ResembleAudioEnhancer local,
        IEnhancerWorkerBroker workers) : IAudioEnhancer
    {
        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            workers.HasLiveWorker || await local.IsAvailableAsync(cancellationToken);

        public async Task<EnhancerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            var status = await local.GetStatusAsync(cancellationToken);
            if (status.State != EnhancerState.Ready && workers.HasLiveWorker)
            {
                return new EnhancerStatus(
                    EnhancerState.Ready, 100, null, ResembleAudioEnhancer.PipelineVersion, null, null);
            }

            return status;
        }

        public Task<EnhancerStatus> ProvisionAsync(CancellationToken cancellationToken = default) =>
            local.ProvisionAsync(cancellationToken);

        public async Task EnhanceChunkAsync(
            string inputWavPath,
            string outputWavPath,
            TimeSpan chunkDuration,
            CancellationToken cancellationToken = default)
        {
            if (await workers.TryEnhanceAsync(inputWavPath, outputWavPath, chunkDuration, cancellationToken))
            {
                return;
            }

            await local.EnhanceChunkAsync(inputWavPath, outputWavPath, chunkDuration, cancellationToken);
        }
    }
}
