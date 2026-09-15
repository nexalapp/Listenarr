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
using Listenarr.Application.Audiobooks.Suggestions;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    /// <summary>
    /// What the library is missing, worked out from catalogs already cached. Reading
    /// never goes to the network; only an explicit refresh does.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/suggestions")]
    [Tags("Library")]
    public sealed class SuggestionsController(
        ISuggestionService suggestions,
        ISuggestionRefreshService refresh) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<SuggestionSnapshot>> Get(CancellationToken cancellationToken = default) =>
            Ok(await suggestions.GetAsync(cancellationToken));

        [HttpGet("refresh")]
        public ActionResult<SuggestionRefreshStatus> RefreshStatus() => Ok(refresh.Status);

        /// <summary>
        /// Fetch catalogs for every library author and series that has none or whose
        /// cached one is stale. Returns 202 with the status; 409 when one is already running.
        /// </summary>
        [HttpPost("refresh")]
        public async Task<ActionResult<SuggestionRefreshStatus>> Refresh(CancellationToken cancellationToken = default)
        {
            if (!await refresh.RequestRefreshAsync(cancellationToken: cancellationToken))
            {
                return Conflict(refresh.Status);
            }

            return Accepted(refresh.Status);
        }
    }
}
