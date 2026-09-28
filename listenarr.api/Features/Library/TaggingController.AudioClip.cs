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
using System.Diagnostics;
using System.Globalization;
using Listenarr.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    public sealed partial class TaggingController
    {
        /// <summary>The longest stretch this will cut, so a request cannot ask for a whole book.</summary>
        private const int LongestClipSeconds = 300;

        /// <summary>
        /// Cuts one short stretch out of a registered file and streams it on its own.
        /// </summary>
        /// <remarks>
        /// The whole-file endpoint beside this one can be seeked, but a player pointed at
        /// it still shows an eight-hour scrubber for a ninety-second question, which is no
        /// use for checking a verdict about how a book ends. This hands back only the
        /// stretch asked for, so the control reads 0:00 to 1:30.
        /// <para>
        /// The seek goes before the input so ffmpeg jumps by index rather than decoding
        /// eight hours to reach the end, and the output is mp3 because every browser plays
        /// it and the alternative is remuxing a container whose index lives at the end of
        /// a stream that is being produced as it goes.
        /// </para>
        /// </remarks>
        /// <param name="fileId">The registered audiobook file to cut from.</param>
        /// <param name="startSeconds">Where the stretch begins.</param>
        /// <param name="seconds">How long it runs, capped.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <response code="200">The stretch, as mp3.</response>
        /// <response code="404">No such file, or it is not readable from here.</response>
        /// <response code="400">Not an audio file, or the bounds make no sense.</response>
        /// <response code="503">No ffmpeg to cut with.</response>
        [HttpGet("files/{fileId:int}/audio/clip")]
        public async Task<IActionResult> GetAudioClip(
            int fileId,
            [FromQuery] double startSeconds = 0,
            [FromQuery] double seconds = 90,
            CancellationToken cancellationToken = default)
        {
            if (startSeconds < 0 || seconds <= 0)
            {
                return BadRequest(new { message = "A clip starts at or after zero and runs for more than nothing." });
            }

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
            if (fullPath == null || !FileUtils.IsAudioFile(fullPath))
            {
                return BadRequest(new { message = "That file is not an audio file." });
            }

            if (!fileSystem.FileExists(fullPath))
            {
                return NotFound(new { message = "File not found." });
            }

            var ffmpeg = await ffmpegService.GetFfmpegPathAsync();
            if (string.IsNullOrWhiteSpace(ffmpeg))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "No ffmpeg is installed to cut a clip with." });
            }

            var length = Math.Min(seconds, LongestClipSeconds);
            var start = startSeconds.ToString("F3", CultureInfo.InvariantCulture);
            var run = length.ToString("F3", CultureInfo.InvariantCulture);

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
                     {
                         "-hide_banner", "-nostdin", "-loglevel", "error",
                         "-ss", start, "-t", run, "-i", fullPath,
                         "-vn", "-ac", "1", "-c:a", "libmp3lame", "-b:a", "64k",
                         "-f", "mp3", "pipe:1"
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo);
            if (process == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "ffmpeg would not start." });
            }

            // Drained so a full stderr pipe cannot wedge the encode, and discarded: a clip
            // that fails simply plays as nothing.
            _ = process.StandardError.ReadToEndAsync(cancellationToken);
            Response.Headers.CacheControl = "private, max-age=3600";
            return File(process.StandardOutput.BaseStream, "audio/mpeg");
        }
    }
}
