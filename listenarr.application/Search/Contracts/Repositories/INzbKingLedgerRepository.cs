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
namespace Listenarr.Application.Search.Contracts.Repositories
{
    /// <summary>
    /// Result of attempting to deduct a token.
    /// </summary>
    /// <param name="Spent">False when the reserve would have been breached.</param>
    /// <param name="BalanceAfter">Estimated balance after the attempt.</param>
    /// <param name="RefillAnchor">Anchor unclaimed refills now accrue from.</param>
    /// <param name="AccessId">Ledger row recording this attempt.</param>
    /// <param name="KeyDeleted">True when the key is already known to be gone.</param>
    public sealed record NzbKingSpendResult(
        bool Spent,
        int BalanceAfter,
        DateTime RefillAnchor,
        int AccessId,
        bool KeyDeleted);

    /// <summary>
    /// Persistence for the NZBKing token ledger.
    ///
    /// <see cref="TrySpendAsync"/> carries the accrue-check-deduct-log sequence because it
    /// has to be atomic: two concurrent grabs that each read the balance before either
    /// writes would both believe they could spend, and double-spending is how the key gets
    /// deleted.
    /// </summary>
    public interface INzbKingLedgerRepository
    {
        Task<NzbKingKeyState?> GetByFingerprintAsync(string fingerprint, CancellationToken ct = default);

        /// <summary>
        /// Atomically accrues refills, refuses or deducts a token, and records the attempt.
        /// </summary>
        Task<NzbKingSpendResult> TrySpendAsync(
            string fingerprint,
            NzbKingAccessPurpose purpose,
            string? query,
            DateTime now,
            CancellationToken ct = default);

        /// <summary>
        /// Records how a previously granted attempt actually ended.
        /// </summary>
        Task RecordOutcomeAsync(
            int accessId,
            string fingerprint,
            NzbKingAccessOutcome outcome,
            int httpStatus,
            DateTime now,
            CancellationToken ct = default);

        /// <summary>
        /// Live keys idle long enough to risk deletion for inactivity.
        /// </summary>
        Task<List<NzbKingKeyState>> GetKeysDueForKeepaliveAsync(DateTime now, CancellationToken ct = default);
    }
}
