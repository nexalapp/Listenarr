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
    /// One key's standing in the pool, as the spend order sees it.
    /// </summary>
    /// <param name="Fingerprint">Ledger identity of the key; never the key itself.</param>
    /// <param name="Balance">Estimated balance, refills already accrued.</param>
    /// <param name="Deleted">Whether NZBKing is known to have deleted the key.</param>
    public readonly record struct NzbKingPoolMember(string Fingerprint, int Balance, bool Deleted);

    /// <summary>
    /// Decides which of several NZBKing keys a request should be charged to.
    ///
    /// Each key NZBKing issues carries its own allowance, so N keys are N allowances
    /// rather than one larger one, and something has to choose between them.
    ///
    /// The choice is the fullest key, which is self-equalising: spending from the maximum
    /// keeps every key at much the same level rather than draining one at a time. That
    /// matters because of how refills work. <see cref="NzbKingTokenPolicy.Accrue"/>
    /// advances the anchor while the balance sits at the cap, so an hour that passes with
    /// a key full earns that key nothing - the pool regenerates one token an hour for each
    /// key *below* the cap, not one an hour per key. Draining a single key and parking the
    /// rest at 100 therefore buys a large burst and no extra throughput, while keeping the
    /// keys level keeps all of them earning. It also keeps every key as far from its
    /// reserve as the total spend allows, which is where the estimate is most likely to be
    /// wrong.
    ///
    /// Kept free of clocks, storage and the keys themselves so the ordering can be tested
    /// directly; callers accrue first and map the fingerprints back to keys afterwards.
    /// </summary>
    public static class NzbKingKeyPool
    {
        /// <summary>
        /// The order in which keys should be tried, fullest live key first.
        ///
        /// A deleted key sorts last rather than being dropped: it cannot be spent, but
        /// attempting it is what records the attempt in the ledger and what tells an
        /// operator the pool is gone rather than merely empty. Ties break on the
        /// fingerprint so the order is deterministic, which keeps a pool of equal keys
        /// rotating predictably instead of depending on dictionary order.
        /// </summary>
        public static IReadOnlyList<NzbKingPoolMember> SpendOrder(IEnumerable<NzbKingPoolMember> members)
        {
            ArgumentNullException.ThrowIfNull(members);

            return members
                .OrderBy(member => member.Deleted)
                .ThenByDescending(member => member.Balance)
                .ThenBy(member => member.Fingerprint, StringComparer.Ordinal)
                .ToList();
        }
    }
}
