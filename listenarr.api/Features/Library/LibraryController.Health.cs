/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Application.Audiobooks.Quality;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    public partial class LibraryController
    {
        /// <summary>
        /// Re-measure the book's health facts now: re-read its files' tags against the
        /// record, re-derive its expected paths, and queue a fresh audio-quality probe.
        /// The page shows stored facts; this is the button for when they look stale.
        /// </summary>
        /// <param name="id">Audiobook ID.</param>
        /// <param name="tagIndex">Re-reads tags and re-derives expected paths.</param>
        /// <param name="prober">Queues the audio-quality probe.</param>
        /// <param name="audiobooks">Finds the book.</param>
        /// <param name="cancellationToken">Abandons the file reads if the caller goes away.</param>
        [HttpPost("{id}/health/refresh")]
        public async Task<IActionResult> RefreshHealth(
            int id,
            [FromServices] ILibraryTagIndexService tagIndex,
            [FromServices] IAudioQualityService prober,
            [FromServices] IAudiobookRepository audiobooks,
            CancellationToken cancellationToken)
        {
            var audiobook = await audiobooks.GetByIdAsync(id);
            if (audiobook == null)
            {
                return NotFound(new { message = "Audiobook not found" });
            }

            // Tags, naming and chapters are re-measured inline — a stat per file. The
            // probe decodes minutes of audio, so it goes through its queue like always.
            await tagIndex.BuildAsync(refresh: true, cancellationToken, audiobookIds: [id]);
            var probe = await prober.EnqueueAsync(id, TagTrigger.Manual, cancellationToken);

            return Ok(new { refreshed = true, probeQueued = probe.Queued });
        }
    }
}
