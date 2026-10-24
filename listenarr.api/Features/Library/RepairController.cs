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
using Listenarr.Application.Audiobooks.Conversion;
using Listenarr.Application.Audiobooks.Repair;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    /// <summary>What to preview: which file and where in it, both optional.</summary>
    public sealed record RepairPreviewRequest(int? FileId = null, double? StartSeconds = null);

    /// <summary>
    /// Audio repair: the generative enhancement of a damaged book, behind a listen-first
    /// workflow. Job control (cancel, retry, dismiss, Activity) lives on the conversion
    /// endpoints — a repair is a conversion job of a different kind.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/repair")]
    [Tags("Library")]
    public sealed class RepairController(
        IConversionQueueService conversionQueue,
        IRepairPreviewService previews,
        IAudioEnhancer enhancer,
        ILogger<RepairController> logger) : ControllerBase
    {
        /// <summary>Where the enhancement model stands: provisioned, downloading, failed.</summary>
        [HttpGet("enhancer")]
        public async Task<IActionResult> GetEnhancerStatus(CancellationToken cancellationToken) =>
            Ok(Project(await enhancer.GetStatusAsync(cancellationToken)));

        /// <summary>
        /// Download and install the enhancement model (about 4 GB into the config volume,
        /// once). Returns at once; poll the status.
        /// </summary>
        /// <response code="202">Provisioning is running or already done.</response>
        [HttpPost("enhancer/provision")]
        public async Task<IActionResult> Provision(CancellationToken cancellationToken) =>
            Accepted(Project(await enhancer.ProvisionAsync(cancellationToken)));

        /// <summary>
        /// Enhance ninety seconds of the book and keep both versions for an A/B listen.
        /// The listener's ear is the acceptance test — measured cleanliness has been
        /// overruled by ear before, once for a chain that measured best of all and was
        /// unintelligible.
        /// </summary>
        /// <response code="202">The preview started; poll it.</response>
        /// <response code="404">No such audiobook, or it has no audio files.</response>
        /// <response code="503">The enhancement model is not ready; the reason says where it stands.</response>
        [HttpPost("audiobooks/{audiobookId:int}/preview")]
        public async Task<IActionResult> StartPreview(
            int audiobookId,
            [FromBody] RepairPreviewRequest? request,
            CancellationToken cancellationToken)
        {
            try
            {
                var preview = await previews.StartAsync(
                    audiobookId,
                    request?.FileId,
                    request?.StartSeconds,
                    cancellationToken);
                return Accepted(Project(preview));
            }
            catch (EnhancerUnavailableException unavailable)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new { reason = unavailable.Message });
            }
            catch (InvalidOperationException invalid)
            {
                return NotFound(new { reason = invalid.Message });
            }
        }

        /// <response code="200">The preview's standing.</response>
        /// <response code="404">No such preview; it may have expired — they live a day.</response>
        [HttpGet("previews/{previewId:guid}")]
        public async Task<IActionResult> GetPreview(Guid previewId, CancellationToken cancellationToken)
        {
            var preview = await previews.GetAsync(previewId, cancellationToken);
            return preview == null
                ? NotFound(new { reason = "No such preview. Previews expire after a day; start another." })
                : Ok(Project(preview));
        }

        /// <summary>One of a ready preview's two clips, encoded exactly as the repair will encode.</summary>
        /// <response code="200">The audio.</response>
        /// <response code="404">No such preview, or it is not ready yet.</response>
        [HttpGet("previews/{previewId:guid}/clip")]
        public IActionResult GetPreviewClip(Guid previewId, [FromQuery] string variant)
        {
            var path = previews.GetClipPath(previewId, variant);
            if (path == null)
            {
                return NotFound(new { reason = "That clip is not ready. Poll the preview until it is." });
            }

            // A real file on disk, so range requests and seeking come for free — an A/B
            // is all seeking.
            return PhysicalFile(path, "audio/mp4", enableRangeProcessing: true);
        }

        /// <summary>
        /// Queue the full repair: every source file through the model, re-encoded as one
        /// M4B, originals retired per the repair disposition. Hours to days of CPU; the
        /// job reports per-file progress in Activity and survives restarts.
        /// </summary>
        /// <response code="202">The repair was queued.</response>
        /// <response code="404">No such audiobook.</response>
        /// <response code="409">Already queued (or converting), nothing to repair, or repair is off.</response>
        /// <response code="503">No encoder, or the enhancement model is not ready.</response>
        [HttpPost("audiobooks/{audiobookId:int}")]
        public async Task<IActionResult> Repair(int audiobookId, CancellationToken cancellationToken)
        {
            var result = await conversionQueue.EnqueueRepairAsync(audiobookId, cancellationToken);
            logger.LogInformation(
                "Repair request for audiobook {AudiobookId}: {Outcome}",
                audiobookId,
                result.Outcome);

            return result.Outcome switch
            {
                ConversionEnqueueOutcome.Queued => Accepted(new { jobId = result.JobId, queued = true }),
                ConversionEnqueueOutcome.NotFound => NotFound(new { queued = false, reason = result.Reason }),
                ConversionEnqueueOutcome.EncoderUnavailable or ConversionEnqueueOutcome.EnhancerUnavailable =>
                    StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        new { queued = false, reason = result.Reason }),
                _ => Conflict(new { queued = false, jobId = result.JobId, reason = result.Reason })
            };
        }

        private static object Project(EnhancerStatus status) => new
        {
            state = status.State.ToString(),
            provisionProgress = status.ProvisionProgress,
            provisionStep = status.ProvisionStep,
            pipelineVersion = status.PipelineVersion,
            diskBytes = status.DiskBytes,
            error = status.Error
        };

        private static object Project(RepairPreview preview) => new
        {
            previewId = preview.Id,
            audiobookId = preview.AudiobookId,
            state = preview.State.ToString(),
            detail = preview.Detail,
            ready = preview.State == RepairPreviewState.Ready
        };
    }
}
