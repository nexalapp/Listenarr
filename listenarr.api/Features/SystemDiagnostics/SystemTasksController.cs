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
using Listenarr.Application.Common.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.SystemDiagnostics
{
    /// <summary>
    /// What every background worker is doing and when it will next do it.
    ///
    /// <para>
    /// Listenarr runs twenty-three loops on staggered delays, and until now the only way
    /// to know whether one was working or merely waiting was to read the container log.
    /// "I ticked it and nothing happened" was the same sentence whether a switch was off,
    /// a five-minute startup delay was running, or an hourly interval had sixty minutes
    /// left on it. This endpoint is so the page can tell them apart.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/system/tasks")]
    [Tags("System")]
    public sealed class SystemTasksController(IWorkerScheduleRegistry schedules) : ControllerBase
    {
        [HttpGet]
        public ActionResult<object> Get() =>
            Ok(schedules.Snapshot().Select(task => new
            {
                name = task.Name,
                intervalSeconds = (int)task.Interval.TotalSeconds,
                lastStartedAt = task.LastStartedAt,
                lastDurationSeconds = task.LastDuration?.TotalSeconds,
                lastOutcome = task.LastOutcome.ToString(),
                nextDueAt = task.NextDueAt,
                running = task.Running
            }));

        /// <summary>
        /// Wakes a sleeping worker so its next cycle starts now.
        ///
        /// It wakes the loop and nothing more. A worker that keeps an interval of its own
        /// - auto-grab counts an hour between runs whoever asked - will wake, consult its
        /// own clock and go back to sleep. Those carry a run-now endpoint of their own,
        /// and the page points at it.
        /// </summary>
        [HttpPost("{name}/run")]
        public ActionResult<object> Run(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { message = "A task name is required." });
            }

            return schedules.Wake(name)
                ? Ok(new { woken = true, name })
                : NotFound(new { message = $"No worker called '{name}' has registered this run." });
        }
    }
}
