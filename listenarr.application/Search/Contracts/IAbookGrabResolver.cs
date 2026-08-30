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
using Listenarr.Application.Search.AbookLink;
using Listenarr.Application.Search.Nzb;

namespace Listenarr.Application.Search.Contracts
{
    /// <summary>
    /// Everything that happened while turning a topic into a downloadable NZB.
    ///
    /// Recorded whether or not it worked. A stalled grab is only actionable if it says
    /// which stage stopped and what it saw, and each stage that can fail leaves the one
    /// input a person could supply to get past it.
    /// </summary>
    /// <param name="TopicId">The topic that was grabbed.</param>
    /// <param name="Succeeded">Whether an NZB was obtained.</param>
    /// <param name="Stage">Where it got to, named for a person rather than a state machine.</param>
    /// <param name="Detail">What happened, in words that suggest what to do next.</param>
    /// <param name="Thanked">Whether a thanks was posted during this grab.</param>
    /// <param name="Post">What the post yielded.</param>
    /// <param name="Resolution">Every index asked, and its answer.</param>
    /// <param name="NzbUrl">Where to fetch the NZB.</param>
    /// <param name="Password">Archive password, when the post carried one.</param>
    public sealed record AbookGrabResult(
        int TopicId,
        bool Succeeded,
        string Stage,
        string Detail,
        bool Thanked,
        AbookPost? Post,
        NzbResolution? Resolution,
        string? NzbUrl,
        string? Password);

    /// <summary>
    /// Resolves a topic to a downloadable NZB.
    ///
    /// This is the only path that posts a thanks, which is publicly attributed to the
    /// configured account, so it runs on explicit request and never during a search.
    /// </summary>
    public interface IAbookGrabResolver
    {
        Task<AbookGrabResult> ResolveAsync(int topicId, CancellationToken ct = default);
    }
}
