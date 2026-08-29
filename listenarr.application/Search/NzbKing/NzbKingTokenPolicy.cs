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
namespace Listenarr.Application.Search.NzbKing
{
    /// <summary>
    /// Estimated balance at a point in time, together with the anchor that unclaimed
    /// refills accrue from.
    /// </summary>
    public readonly record struct NzbKingBalance(int Balance, DateTime RefillAnchor);

    /// <summary>
    /// NZBKing's token rules, expressed as pure functions.
    ///
    /// NZBKing grants a key 100 tokens, deducts one per query, and returns one per hour
    /// while below the cap. Hitting zero deletes the key, which can only be replaced by a
    /// human solving a CAPTCHA — so the cost of overspending is far higher than the cost
    /// of refusing a query, and this policy is deliberately pessimistic.
    ///
    /// Kept free of clocks and storage so the arithmetic can be tested exhaustively;
    /// callers pass <c>now</c> in and persist what comes out.
    /// </summary>
    public static class NzbKingTokenPolicy
    {
        /// <summary>Ceiling NZBKing refills towards.</summary>
        public const int MaxTokens = 100;

        /// <summary>
        /// Tokens never spent. Our count is an estimate and can drift below NZBKing's
        /// true figure; a reserve of one would leave drift no room, and being wrong once
        /// costs the key. Five is cheap insurance.
        /// </summary>
        public const int ReserveFloor = 5;

        /// <summary>How often NZBKing returns a token while below the cap.</summary>
        public static readonly TimeSpan RefillInterval = TimeSpan.FromHours(1);

        /// <summary>
        /// Idle period after which a key is touched. NZBKing deletes a key unused for a
        /// month; 28 days leaves room for a missed cycle.
        /// </summary>
        public static readonly TimeSpan KeepaliveAfter = TimeSpan.FromDays(28);

        /// <summary>
        /// Applies whole hours of refill accrued since <paramref name="refillAnchor"/>.
        ///
        /// The anchor advances by the hours consumed rather than jumping to
        /// <paramref name="now"/>, so a partial hour carries forward instead of being lost
        /// across successive calls. While the balance sits at the cap the anchor still
        /// advances, which is what stops an idle key from banking a refill backlog it
        /// never actually earned.
        /// </summary>
        public static NzbKingBalance Accrue(int balance, DateTime refillAnchor, DateTime now)
        {
            var elapsed = now - refillAnchor;
            if (elapsed <= TimeSpan.Zero)
            {
                // Clock moved backwards, or no time has passed. Never grant, and never
                // move the anchor backwards.
                return new NzbKingBalance(Clamp(balance), refillAnchor);
            }

            var wholeIntervals = (long)(elapsed.Ticks / RefillInterval.Ticks);
            if (wholeIntervals <= 0)
            {
                return new NzbKingBalance(Clamp(balance), refillAnchor);
            }

            var clamped = Clamp(balance);
            var headroom = MaxTokens - clamped;
            var granted = (int)Math.Min(wholeIntervals, headroom);

            return new NzbKingBalance(
                clamped + granted,
                refillAnchor + TimeSpan.FromTicks(RefillInterval.Ticks * wholeIntervals));
        }

        /// <summary>
        /// Whether one token may be spent without dropping into the reserve.
        /// </summary>
        public static bool CanSpend(int balance) => balance - 1 >= ReserveFloor;

        /// <summary>
        /// When the next token lands, for telling an operator how long to wait.
        /// </summary>
        public static DateTime NextRefillAt(DateTime refillAnchor) => refillAnchor + RefillInterval;

        /// <summary>
        /// Whether an unused key should be touched to stop NZBKing deleting it.
        /// A key never used successfully is measured from when it was first recorded.
        /// </summary>
        public static bool IsDueForKeepalive(DateTime? lastSuccessfulUseAt, DateTime createdAt, DateTime now)
            => now - (lastSuccessfulUseAt ?? createdAt) >= KeepaliveAfter;

        private static int Clamp(int balance) => Math.Clamp(balance, 0, MaxTokens);
    }
}
