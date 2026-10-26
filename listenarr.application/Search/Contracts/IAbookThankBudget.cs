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
namespace Listenarr.Application.Search.Contracts
{
    /// <summary>
    /// Whether a thank may be pressed now, and why not when it may not.
    /// </summary>
    /// <param name="Allowed">True when the caller may thank.</param>
    /// <param name="Reason">Why it was refused; null when allowed.</param>
    /// <param name="ThanksThisHour">How many have been pressed in the trailing hour.</param>
    /// <param name="NextAllowedAt">When the budget next has room, if it is the rate that refused.</param>
    public sealed record AbookThankDecision(
        bool Allowed,
        string? Reason,
        int ThanksThisHour,
        DateTime? NextAllowedAt);

    /// <summary>
    /// The budget for abook.link's thank button.
    ///
    /// It sits at the resolver rather than on any one caller because several reach it -
    /// auto-grab, the automatic search for monitored books, a manual add, a retry after a
    /// failed download - and a limit on one of them is not a limit at all. Measured on
    /// 2026-10-26: auto-grab was adding ten books an hour while ninety-nine thanks were
    /// pressed in three and a half hours.
    ///
    /// It is a safety rail, not a disguise. The forum ranks thanks received, never thanks
    /// given, and runs a posting bot of its own; nothing here is hiding from anyone. What
    /// it prevents is a loop or a retry storm hammering somebody's forum.
    /// </summary>
    public interface IAbookThankBudget
    {
        /// <summary>Asks for room to press thank once, without recording one.</summary>
        Task<AbookThankDecision> CheckAsync(CancellationToken ct = default);

        /// <summary>Records what a thank did, which is what later checks read.</summary>
        Task RecordAsync(int topicId, bool succeeded, string? detail, CancellationToken ct = default);

        /// <summary>What the panel shows: the trailing hour and the day so far.</summary>
        Task<(int Hour, int Day)> CountsAsync(CancellationToken ct = default);
    }
}
