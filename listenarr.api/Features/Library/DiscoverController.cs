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
using Listenarr.Application.Audiobooks.Discover;
using Listenarr.Domain.Audiobooks.Discover;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    /// <summary>
    /// The Suggested page's outward-looking tabs: Audible's charts, and what has just
    /// been posted to the abook.link boards you follow.
    ///
    /// <para>
    /// Reading is answered from the cache and never goes to a source. Refreshing does,
    /// and is asynchronous: it returns at once with the status the page polls.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/discover")]
    [Tags("Library")]
    public sealed class DiscoverController(
        IDiscoverService discover,
        IDiscoverRefreshService refresh) : ControllerBase
    {
        /// <summary>The short names the routes use, so a URL is not a stored constant.</summary>
        private static readonly IReadOnlyDictionary<string, string> Sources =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["charts"] = DiscoverSources.AudibleCharts,
                ["abook"] = DiscoverSources.AbookLink
            };

        [HttpGet("{source}")]
        public async Task<ActionResult<DiscoverFeed>> Get(string source, CancellationToken cancellationToken = default)
        {
            if (!Sources.TryGetValue(source, out var resolved))
            {
                return NotFound(new { message = $"There is no discovery source called '{source}'." });
            }

            return Ok(await discover.GetFeedAsync(resolved, cancellationToken));
        }

        /// <summary>
        /// What the source offers to follow. This one reads the source's own index — it
        /// is the picker opening — and the answer is cached for a few minutes.
        /// </summary>
        [HttpGet("{source}/sections")]
        public async Task<ActionResult<DiscoverSectionCatalog>> Sections(
            string source,
            CancellationToken cancellationToken = default)
        {
            if (!Sources.TryGetValue(source, out var resolved))
            {
                return NotFound(new { message = $"There is no discovery source called '{source}'." });
            }

            return Ok(await discover.GetSectionsAsync(resolved, cancellationToken));
        }

        [HttpPut("{source}/sections")]
        public async Task<ActionResult<DiscoverSelection>> SaveSections(
            string source,
            [FromBody] SaveDiscoverSelectionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!Sources.TryGetValue(source, out var resolved))
            {
                return NotFound(new { message = $"There is no discovery source called '{source}'." });
            }

            if (request is null)
            {
                return BadRequest(new { message = "A selection is required." });
            }

            return Ok(await discover.SaveSelectionAsync(resolved, request, cancellationToken));
        }

        [HttpGet("{source}/refresh")]
        public ActionResult<DiscoverRefreshStatus> RefreshStatus(string source) =>
            Sources.TryGetValue(source, out var resolved)
                ? Ok(refresh.StatusFor(resolved))
                : NotFound(new { message = $"There is no discovery source called '{source}'." });

        /// <summary>
        /// Re-read the head of every followed section. 202 with the status; 409 when a
        /// fetch is already running or nothing is followed.
        /// </summary>
        [HttpPost("{source}/refresh")]
        public async Task<ActionResult<DiscoverRefreshStatus>> Refresh(
            string source,
            CancellationToken cancellationToken = default)
        {
            if (!Sources.TryGetValue(source, out var resolved))
            {
                return NotFound(new { message = $"There is no discovery source called '{source}'." });
            }

            return await refresh.RequestRefreshAsync(resolved, cancellationToken)
                ? Accepted(refresh.StatusFor(resolved))
                : Conflict(refresh.StatusFor(resolved));
        }

        /// <summary>
        /// Read one more page of every followed section. 409 when a fetch is running, or
        /// when every section is already as deep as the setting allows.
        /// </summary>
        [HttpPost("{source}/more")]
        public async Task<ActionResult<DiscoverRefreshStatus>> More(
            string source,
            CancellationToken cancellationToken = default)
        {
            if (!Sources.TryGetValue(source, out var resolved))
            {
                return NotFound(new { message = $"There is no discovery source called '{source}'." });
            }

            return await refresh.RequestMoreAsync(resolved, cancellationToken)
                ? Accepted(refresh.StatusFor(resolved))
                : Conflict(refresh.StatusFor(resolved));
        }
    }
}
