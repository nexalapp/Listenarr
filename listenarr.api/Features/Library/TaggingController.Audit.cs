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
using Listenarr.Application.Audiobooks.Audit;
using Listenarr.Application.Audiobooks.Transcription;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    public sealed partial class TaggingController
    {
        /// <summary>
        /// Queue an audio audit: listen to the book's opening and closing and record
        /// whether they agree with its record. The verdict lands on the book.
        /// </summary>
        /// <response code="202">The audit was queued.</response>
        /// <response code="404">No such audiobook.</response>
        /// <response code="409">Already queued, no audio files, or transcription is off.</response>
        [HttpPost("audiobooks/{audiobookId:int}/audit")]
        public async Task<IActionResult> AuditAudio(
            int audiobookId,
            [FromServices] IAudioAuditService auditor,
            CancellationToken cancellationToken = default)
        {
            var result = await auditor.EnqueueAsync(audiobookId, TagTrigger.Manual, cancellationToken);
            logger.LogInformation("Audio audit request for audiobook {AudiobookId}: {Outcome}", audiobookId, result.Outcome);
            return ToResponse(result);
        }

        /// <summary>
        /// Where the whisper model stands: on disk, downloading, missing, or failed. Asks
        /// about the configured model unless <c>model</c> names another.
        /// </summary>
        /// <response code="200">The model's status.</response>
        [HttpGet("transcription/model")]
        public async Task<IActionResult> GetTranscriptionModel(
            [FromServices] ITranscriber? transcriber,
            [FromQuery] string? model = null,
            CancellationToken cancellationToken = default)
        {
            if (transcriber == null)
            {
                return Ok(new { model, state = "missing", sizeBytes = (long?)null, error = "Transcription is not built into this server." });
            }

            return Ok(ToModelResponse(await transcriber.GetModelStatusAsync(model, cancellationToken)));
        }

        /// <summary>
        /// Start downloading a whisper model so the first transcription does not wait on
        /// it. Returns at once with the model's status; poll the GET to watch it land.
        /// </summary>
        /// <response code="200">The download is running, or the model is already on disk.</response>
        /// <response code="400">Not a model this server knows.</response>
        [HttpPost("transcription/model")]
        public async Task<IActionResult> DownloadTranscriptionModel(
            [FromServices] ITranscriber? transcriber,
            [FromBody] DownloadModelRequest request,
            CancellationToken cancellationToken = default)
        {
            if (transcriber == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason = "Transcription is not built into this server." });
            }

            try
            {
                var status = await transcriber.DownloadModelAsync(request.Model, cancellationToken);
                logger.LogInformation("Whisper model {Model} download requested: {State}", request.Model, status.State);
                return Ok(ToModelResponse(status));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { reason = ex.Message });
            }
        }

        private static object ToModelResponse(TranscriptionModelStatus status) => new
        {
            model = status.Model,
            state = status.State.ToString().ToLowerInvariant(),
            sizeBytes = status.SizeBytes,
            error = status.Error
        };
    }

    public sealed class DownloadModelRequest
    {
        public string Model { get; set; } = "base.en";
    }
}
