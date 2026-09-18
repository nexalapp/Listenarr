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
    }
}
