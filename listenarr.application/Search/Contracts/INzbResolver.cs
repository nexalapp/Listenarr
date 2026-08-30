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
using Listenarr.Application.Search.Nzb;

namespace Listenarr.Application.Search.Contracts
{
    /// <summary>
    /// Turns a search string from a forum post into an NZB.
    /// </summary>
    public interface INzbResolver
    {
        /// <summary>Name shown to an operator, e.g. "NZBIndex".</summary>
        string Name { get; }

        /// <summary>
        /// Order within the chain, lowest first. Free indexes come before metered ones so
        /// an allowance is only spent when the free ones have nothing.
        /// </summary>
        int Order { get; }

        Task<NzbResolverResult> ResolveAsync(string searchString, CancellationToken ct = default);
    }

    /// <summary>
    /// Asks each resolver in turn until one produces an NZB, recording every answer.
    /// </summary>
    public interface INzbResolverChain
    {
        Task<NzbResolution> ResolveAsync(string? searchString, CancellationToken ct = default);
    }
}
