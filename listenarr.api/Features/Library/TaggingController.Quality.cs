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
using Listenarr.Application.Audiobooks.Quality;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    public sealed partial class TaggingController
    {
        /// <summary>
        /// Queue an audio-quality probe: decode a couple of minutes of the book and
        /// record what the recording sounds like. The verdict lands on the book.
        /// </summary>
        /// <response code="202">The probe was queued.</response>
        /// <response code="404">No such audiobook.</response>
        /// <response code="409">Already queued, or the book has no audio files.</response>
        [HttpPost("audiobooks/{audiobookId:int}/probe")]
        public async Task<IActionResult> ProbeAudioQuality(
            int audiobookId,
            [FromServices] IAudioQualityService prober,
            CancellationToken cancellationToken = default)
        {
            var result = await prober.EnqueueAsync(audiobookId, TagTrigger.Manual, cancellationToken);
            logger.LogInformation(
                "Audio-quality probe request for audiobook {AudiobookId}: {Outcome}",
                audiobookId,
                result.Outcome);
            return ToResponse(result);
        }
    }
}
