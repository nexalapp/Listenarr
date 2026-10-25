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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Tagging
{
    public sealed partial class TagJobProcessor
    {
        /// <summary>
        /// The probe kind: decode a couple of minutes, judge the recording, store five
        /// columns. No scratch file, no publication.
        /// </summary>
        private async Task<ExecutionOutcome> ExecuteAudioQualityProbeAsync(
            TagJob job,
            Audiobook audiobook,
            IServiceProvider services,
            ITagQueueService queue,
            CancellationToken cancellationToken)
        {
            await queue.ReportProgressAsync(job.Id, TagJobPhase.Reading, 5, cancellationToken);
            var prober = services.GetRequiredService<IAudioQualityService>();
            try
            {
                var report = await prober.ProbeAsync(
                    audiobook.Id,
                    BuildProgress(job.Id, 0, 100),
                    cancellationToken);
                logger.LogInformation(
                    "Audio-quality probe {JobId}: {Verdict} — {Reason}",
                    job.Id,
                    report.Verdict,
                    report.Reason);
                await OfferAutoAudioRepairAsync(job, audiobook, report, services, cancellationToken);
                return ExecutionOutcome.Succeeded(0, job.FileCount, audiobook.Title);
            }
            catch (InvalidOperationException ex)
            {
                return ExecutionOutcome.Failed(TagWriteFailureKind.WriterUnavailable, ex.Message);
            }
            catch (FfmpegException ex)
            {
                return ExecutionOutcome.Failed(TagWriteFailureKind.SourceUnreadable, ex.Message);
            }
        }
    }
}
