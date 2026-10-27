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
using Listenarr.Application.Audiobooks.AutoGrab;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    /// <summary>
    /// Auto-grab: which of the Suggested page's lists are added from without being
    /// asked, in what order, and how many at a time.
    ///
    /// <para>
    /// The preview is the important endpoint. "Grab everything I follow" is not a
    /// sentence anyone should take on trust, so the page can always ask what a run
    /// would do before the switch is thrown.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/autograb")]
    [Tags("Library")]
    public sealed class AutoGrabController(
        IAutoGrabService autoGrab,
        IAutoGrabBackfillService backfill,
        IAbookThankBudget thanks,
        IConfigurationService configuration) : ControllerBase
    {
        [HttpGet("settings")]
        public async Task<ActionResult<AutoGrabSettings>> GetSettings() =>
            Ok(AutoGrabSettingsJson.Read((await configuration.GetApplicationSettingsAsync()).AutoGrabSettingsJson));

        [HttpPut("settings")]
        public async Task<ActionResult<AutoGrabSettings>> SaveSettings(
            [FromBody] AutoGrabSettings request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Settings are required." });
            }

            var clean = AutoGrabSettingsJson.Sanitize(request);
            var settings = await configuration.GetApplicationSettingsAsync();
            settings.AutoGrabSettingsJson = AutoGrabSettingsJson.Write(clean);
            await configuration.SaveApplicationSettingsAsync(settings);
            return Ok(clean);
        }

        /// <summary>What a run would add right now. Adds nothing.</summary>
        [HttpGet("preview")]
        public async Task<ActionResult<AutoGrabPlan>> Preview(CancellationToken cancellationToken = default) =>
            Ok(await autoGrab.PreviewAsync(cancellationToken));

        /// <summary>
        /// Sends the release already found for books that were added but never
        /// downloaded - the backlog left by an add whose grab failed quietly.
        ///
        /// Costs the forum nothing it has not already paid: those posts have been
        /// thanked, so their payload is visible and the resolver does not press the
        /// button again. Capped per call, and the thank budget still applies to any
        /// post that turns out never to have been thanked.
        /// </summary>
        [HttpPost("backfill")]
        public async Task<ActionResult<AutoGrabBackfillReport>> Backfill(
            [FromQuery] int limit = 25,
            CancellationToken cancellationToken = default) =>
            Ok(await backfill.RunAsync(limit, cancellationToken));

        /// <summary>
        /// What the abook.link thank budget has left. A grab from that source has to
        /// press thank, so this is the ceiling on grabs from it - and the page saying so
        /// is the difference between "waiting" and "broken", which has not always been
        /// obvious from the outside.
        /// </summary>
        [HttpGet("thanks")]
        public async Task<ActionResult<object>> Thanks(CancellationToken cancellationToken = default)
        {
            var (hour, day) = await thanks.CountsAsync(cancellationToken);
            var decision = await thanks.CheckAsync(cancellationToken);
            var settings = await configuration.GetApplicationSettingsAsync();

            return Ok(new
            {
                thisHour = hour,
                today = day,
                perHour = settings.AbookThanksPerHour,
                allowed = decision.Allowed,
                reason = decision.Reason,
                nextAllowedAt = decision.NextAllowedAt
            });
        }

        /// <summary>
        /// Runs now, whatever the interval says. Honours the cap, and the switch: a run
        /// asked for by hand still only adds what one run may add.
        /// </summary>
        [HttpPost("run")]
        public async Task<ActionResult<AutoGrabReport>> Run(CancellationToken cancellationToken = default) =>
            Ok(await autoGrab.RunAsync(cancellationToken));
    }
}
