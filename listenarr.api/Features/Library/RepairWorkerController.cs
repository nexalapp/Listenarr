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
using System.Text.RegularExpressions;
using Listenarr.Application.Audiobooks.Repair;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    /// <summary>A worker asking for a chunk; <c>Ready</c> false says it is alive but its card is taken.</summary>
    public sealed record EnhancerWorkerClaimRequest(string WorkerId, bool Ready = true);

    /// <summary>A worker giving a chunk back unfinished.</summary>
    public sealed record EnhancerWorkerFailRequest(string WorkerId, string? Reason);

    /// <summary>
    /// The pull API a remote GPU worker uses to enhance repair chunks. It authenticates
    /// with the API key like any other client; it never names a path — the server knows
    /// which file a task id stands for and hands out only that one, only to the worker
    /// holding it.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/repair/worker")]
    [Tags("Library")]
    public sealed partial class RepairWorkerController(IEnhancerWorkerBroker broker) : ControllerBase
    {
        private static readonly TimeSpan LongPoll = TimeSpan.FromSeconds(20);

        // A worker id appears in logs; keep it to a short, printable name.
        [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
        private static partial Regex WorkerIdPattern();

        private static bool ValidWorker(string? workerId) =>
            workerId != null && WorkerIdPattern().IsMatch(workerId);

        /// <summary>Who is connected, how many chunks each has done, and how fast the last one went.</summary>
        [HttpGet("status")]
        public IActionResult Status() => Ok(broker.Workers);

        /// <summary>
        /// Ask for a chunk, waiting up to twenty seconds for one. Also the heartbeat: a
        /// worker that stops calling this is treated as gone within ninety seconds.
        /// </summary>
        /// <response code="200">A chunk to enhance.</response>
        /// <response code="204">Nothing to do yet; ask again.</response>
        [HttpPost("claim")]
        public async Task<IActionResult> Claim([FromBody] EnhancerWorkerClaimRequest request, CancellationToken cancellationToken)
        {
            if (!ValidWorker(request.WorkerId))
            {
                return BadRequest(new { error = "workerId must be 1-64 letters, digits, '.', '_' or '-'." });
            }

            try
            {
                var task = await broker.ClaimAsync(request.WorkerId, request.Ready, LongPoll, cancellationToken);
                return task == null ? NoContent() : Ok(task);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return NoContent();
            }
        }

        /// <summary>The chunk's audio: mono 44.1 kHz WAV.</summary>
        [HttpGet("tasks/{taskId:guid}/input")]
        public IActionResult Input(Guid taskId, [FromQuery] string workerId)
        {
            if (!ValidWorker(workerId))
            {
                return BadRequest();
            }

            // The path comes from the broker's own record, never from the request.
            var path = broker.InputPathFor(taskId, workerId);
            return path == null ? NotFound() : PhysicalFile(path, "audio/wav");
        }

        /// <summary>The enhanced chunk, as the WAV the model wrote.</summary>
        /// <response code="204">Accepted.</response>
        /// <response code="404">The chunk is no longer this worker's; discard it.</response>
        [HttpPut("tasks/{taskId:guid}/output")]
        [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
        public async Task<IActionResult> Output(Guid taskId, [FromQuery] string workerId, CancellationToken cancellationToken)
        {
            if (!ValidWorker(workerId))
            {
                return BadRequest();
            }

            return await broker.CompleteAsync(taskId, workerId, Request.Body, cancellationToken)
                ? NoContent()
                : NotFound();
        }

        /// <summary>Give a chunk back unfinished; it is offered again.</summary>
        [HttpPost("tasks/{taskId:guid}/fail")]
        public IActionResult Fail(Guid taskId, [FromBody] EnhancerWorkerFailRequest request)
        {
            if (!ValidWorker(request.WorkerId))
            {
                return BadRequest();
            }

            return broker.Fail(taskId, request.WorkerId, request.Reason ?? "no reason given")
                ? NoContent()
                : NotFound();
        }
    }
}
