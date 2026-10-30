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

using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Downloads;

/// <summary>
/// The releases an automatic search passes over, because grabbing them already failed.
///
/// Blocks are made without being asked for, so they have to be visible and reversible:
/// a failure classified wrongly would otherwise take a release away from a book for
/// good, with nothing to show that it had happened.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/downloads/blocklist")]
[Tags("Downloads")]
public class ReleaseBlocklistController(
    IReleaseBlocklistRepository blocklist,
    ILogger<ReleaseBlocklistController> logger) : ControllerBase
{
    /// <summary>The most recently blocked releases, newest first (at most 500).</summary>
    [HttpGet]
    public async Task<ActionResult<List<BlockedRelease>>> GetRecent(
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await blocklist.GetRecentAsync(limit, cancellationToken));

    /// <summary>The releases blocked for one book.</summary>
    [HttpGet("audiobook/{audiobookId:int}")]
    public async Task<ActionResult<List<BlockedRelease>>> GetForAudiobook(
        int audiobookId,
        CancellationToken cancellationToken = default) =>
        Ok(await blocklist.GetForAudiobookAsync(audiobookId, cancellationToken));

    /// <summary>Unblocks one release, so the next search may pick it again.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Unblock(int id, CancellationToken cancellationToken = default)
    {
        if (!await blocklist.DeleteAsync(id, cancellationToken))
        {
            return NotFound(new { error = "Blocked release not found", id });
        }

        logger.LogInformation("Unblocked release {BlockedReleaseId} at the operator's request", id);
        return Ok(new { message = "Release unblocked" });
    }

    /// <summary>
    /// Unblocks every release blocked for one book - the way back when a book has been
    /// left with nothing to grab and the blocks are suspect.
    /// </summary>
    [HttpDelete("audiobook/{audiobookId:int}")]
    public async Task<IActionResult> UnblockAudiobook(
        int audiobookId,
        CancellationToken cancellationToken = default)
    {
        var removed = await blocklist.DeleteForAudiobookAsync(audiobookId, cancellationToken);
        logger.LogInformation(
            "Unblocked {Count} release(s) for audiobook {AudiobookId} at the operator's request",
            removed,
            audiobookId);
        return Ok(new { message = $"Unblocked {removed} release(s)", removed });
    }
}
