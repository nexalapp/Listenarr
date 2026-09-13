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
using Listenarr.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    public sealed partial class TaggingController
    {
        /// <summary>
        /// Streams one registered library file, so the tags table can play it.
        /// </summary>
        /// <remarks>
        /// Addressed by file id rather than by path. The root folder preview takes a path
        /// and has to prove it canonicalizes inside the folder before opening it; there is
        /// no such burden here because the client never names a file — it names a row, and
        /// the path comes from the registration that row was built from.
        /// <para>
        /// Range processing is enabled because the player needs it: an M4B commonly carries
        /// its moov atom at the end, so a browser seeks there before it can decode the
        /// first second, and without ranges it would pull the whole book to do it.
        /// </para>
        /// </remarks>
        /// <param name="fileId">The registered audiobook file to play.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <response code="200">The audio, as a range-capable stream.</response>
        /// <response code="404">No such file, or it is not readable from here.</response>
        /// <response code="400">The registered file is not an audio file.</response>
        [HttpGet("files/{fileId:int}/audio")]
        public async Task<IActionResult> GetAudio(
            int fileId,
            CancellationToken cancellationToken = default)
        {
            var file = await audiobookFileRepository.GetByIdAsync(fileId, cancellationToken);
            if (file == null)
            {
                return NotFound(new { message = "No such file." });
            }

            var audiobook = await audiobookRepository.GetByIdAsync(file.AudiobookId);
            if (audiobook == null)
            {
                return NotFound(new { message = "That file's audiobook no longer exists." });
            }

            var fullPath = AudiobookFilePaths.ResolveFullPath(audiobook, file);
            if (fullPath == null)
            {
                return NotFound(new { message = "That file's path cannot be resolved from here." });
            }

            // A registration is not a promise that the file is audio: the same table holds
            // whatever the scanner attached to the book.
            if (!FileUtils.IsAudioFile(fullPath))
            {
                return BadRequest(new { message = "That file is not an audio file." });
            }

            if (!fileSystem.FileExists(fullPath))
            {
                return NotFound(new { message = "File not found." });
            }

            return new PhysicalFileResult(fullPath, AudioContentTypes.ForFile(fullPath))
            {
                EnableRangeProcessing = true
            };
        }
    }
}
