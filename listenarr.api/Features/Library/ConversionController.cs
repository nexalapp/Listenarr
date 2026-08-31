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
using Listenarr.Domain.Audiobooks.Conversion;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    /// <summary>
    /// Manual control over MP3 to M4B conversion.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/conversion")]
    [Tags("Library")]
    public sealed class ConversionController(
        IConversionQueueService conversionQueue,
        ILogger<ConversionController> logger) : ControllerBase
    {
        /// <summary>
        /// Queue a conversion for a book already in the library as MP3s.
        /// </summary>
        /// <remarks>
        /// A manual request runs regardless of the automatic-conversion setting; the
        /// other refusals (no MP3s, no encoder, already queued) still apply and are
        /// returned with the reason rather than as a generic failure.
        /// </remarks>
        /// <response code="202">The conversion was queued.</response>
        /// <response code="404">No such audiobook.</response>
        /// <response code="409">Already queued, or there is nothing to convert.</response>
        /// <response code="503">No encoder is installed.</response>
        [HttpPost("audiobooks/{audiobookId:int}")]
        public async Task<IActionResult> Convert(
            int audiobookId,
            CancellationToken cancellationToken = default)
        {
            var result = await conversionQueue.EnqueueAsync(
                audiobookId,
                ConversionTrigger.Manual,
                cancellationToken);

            logger.LogInformation(
                "Manual conversion request for audiobook {AudiobookId}: {Outcome}",
                audiobookId,
                result.Outcome);

            return ToResponse(result);
        }

        /// <summary>
        /// Re-run a conversion that failed.
        /// </summary>
        /// <response code="202">The conversion was queued again.</response>
        /// <response code="404">No such conversion job.</response>
        /// <response code="409">That conversion is already running.</response>
        [HttpPost("jobs/{jobId:guid}/retry")]
        public async Task<IActionResult> Retry(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var result = await conversionQueue.RetryAsync(jobId, cancellationToken);
            return ToResponse(result);
        }

        /// <summary>
        /// Every conversion worth showing: active jobs, plus recently finished ones.
        /// </summary>
        [HttpGet("jobs")]
        public async Task<IActionResult> GetJobs(CancellationToken cancellationToken = default)
        {
            var jobs = await conversionQueue.GetVisibleJobsAsync(cancellationToken);
            return Ok(jobs.Select(Project));
        }

        /// <summary>
        /// The active conversion for one book, if there is one. Drives the book page's
        /// button state.
        /// </summary>
        /// <response code="204">No conversion is active for this book.</response>
        [HttpGet("audiobooks/{audiobookId:int}")]
        public async Task<IActionResult> GetForAudiobook(
            int audiobookId,
            CancellationToken cancellationToken = default)
        {
            var job = await conversionQueue.GetActiveJobForAudiobookAsync(audiobookId, cancellationToken);
            return job == null ? NoContent() : Ok(Project(job));
        }

        private IActionResult ToResponse(ConversionEnqueueResult result) => result.Outcome switch
        {
            ConversionEnqueueOutcome.Queued =>
                Accepted(new { jobId = result.JobId, queued = true }),

            ConversionEnqueueOutcome.NotFound =>
                NotFound(new { queued = false, reason = result.Reason }),

            ConversionEnqueueOutcome.EncoderUnavailable =>
                StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new { queued = false, reason = result.Reason }),

            // Already queued and nothing-to-convert are both "the request cannot apply to
            // this book right now", which is what 409 says.
            _ => Conflict(new { queued = false, jobId = result.JobId, reason = result.Reason })
        };

        /// <summary>
        /// The public shape of a job. Deliberately not the entity: lease ownership and
        /// the deduplication key are internal scheduling state.
        /// </summary>
        private static object Project(ConversionJob job) => new
        {
            jobId = job.Id.ToString(),
            audiobookId = job.AudiobookId,
            status = job.Status.ToString(),
            phase = job.Phase.ToString(),
            progress = job.Progress,
            trigger = job.Trigger.ToString(),
            sourceFileCount = job.SourceFileCount,
            chapterCount = job.ChapterCount,
            error = job.Error,
            failureKind = job.FailureKind,
            canRetry = job.CanRetry,
            attemptCount = job.AttemptCount,
            enqueuedAt = job.EnqueuedAt,
            completedAt = job.CompletedAt
        };
    }
}
