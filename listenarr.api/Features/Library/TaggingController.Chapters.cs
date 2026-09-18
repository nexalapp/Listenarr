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
using Listenarr.Application.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Chapters;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    public sealed partial class TaggingController
    {
        /// <summary>
        /// What a chapter repair would write into each of a book's files.
        /// </summary>
        /// <remarks>
        /// A repair replaces the file's chapter structures with a list from another
        /// source, so the list is shown before anything is queued. A file whose chapters
        /// are not corrupt, or for which no source matches the edition, is listed with
        /// the reason and cannot be queued.
        /// </remarks>
        /// <response code="200">The preview.</response>
        /// <response code="404">No such audiobook.</response>
        [HttpGet("audiobooks/{audiobookId:int}/chapters/preview")]
        public async Task<IActionResult> PreviewChapterRepair(
            int audiobookId,
            [FromServices] IChapterRepairService repairService,
            [FromQuery] int[]? fileIds = null,
            CancellationToken cancellationToken = default)
        {
            var preview = await repairService.PreviewAsync(
                audiobookId,
                fileIds is { Length: > 0 } ? fileIds : null,
                cancellationToken);

            if (preview == null)
            {
                return NotFound(new { reason = "That audiobook no longer exists." });
            }

            return Ok(new
            {
                audiobookId = preview.AudiobookId,
                repairable = preview.Files.Any(file => file.Repairable),
                files = preview.Files.Select(file => new
                {
                    fileId = file.FileId,
                    name = file.FileName,
                    chapterHealth = ChapterHealthNames.Of(file.Health),
                    chapterReason = file.HealthReason,
                    repairable = file.Repairable,
                    rejection = file.Rejection,
                    source = file.Plan?.Source.ToString(),
                    partial = file.Plan?.Partial ?? false,
                    note = file.Plan?.Note,
                    chapters = file.Plan?.Chapters.Select((chapter, index) => new
                    {
                        title = chapter.Title,
                        startSeconds = chapter.Start.TotalSeconds,
                        endSeconds = chapter.End.TotalSeconds,
                        // What the narrator said at this mark, when the plan came from listening.
                        heard = file.Plan.Heard != null && index < file.Plan.Heard.Count ? file.Plan.Heard[index] : null
                    })
                })
            });
        }

        /// <summary>
        /// Queue a chapter repair for a book's corrupt files.
        /// </summary>
        /// <response code="202">The repair was queued.</response>
        /// <response code="404">No such audiobook.</response>
        /// <response code="409">Nothing in scope is repairable, or a write is already queued.</response>
        /// <response code="503">No ffmpeg is installed.</response>
        [HttpPost("audiobooks/{audiobookId:int}/chapters")]
        public async Task<IActionResult> RepairChapters(
            int audiobookId,
            [FromServices] IChapterRepairService repairService,
            [FromBody] RepairChaptersRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            var result = await repairService.EnqueueAsync(
                audiobookId,
                request?.FileIds is { Count: > 0 } fileIds ? fileIds : null,
                cancellationToken);

            logger.LogInformation(
                "Chapter repair request for audiobook {AudiobookId}: {Outcome}",
                audiobookId,
                result.Outcome);

            return ToResponse(result);
        }
    }

    /// <summary>Which of the book's files to repair, or null for every corrupt one.</summary>
    public sealed class RepairChaptersRequest
    {
        public List<int>? FileIds { get; set; }
    }
}
