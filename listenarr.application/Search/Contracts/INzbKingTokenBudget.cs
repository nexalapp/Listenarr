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
    /// Permission to make exactly one NZBKing request.
    ///
    /// A granted lease means a token has already been deducted and the attempt logged,
    /// so the request may proceed. A refused lease means nothing was spent and no request
    /// should be made.
    /// </summary>
    /// <param name="Granted">Whether the caller may issue its request.</param>
    /// <param name="KeyFingerprint">Ledger identity of the key this lease draws on.</param>
    /// <param name="BalanceAfter">Estimated balance once this lease is accounted for.</param>
    /// <param name="NextRefillAt">When the next token is expected to land.</param>
    /// <param name="AccessId">Ledger row to reconcile once the request finishes.</param>
    /// <param name="Reason">Operator-facing explanation, populated when refused.</param>
    public sealed record NzbKingTokenLease(
        bool Granted,
        string KeyFingerprint,
        int BalanceAfter,
        DateTime NextRefillAt,
        int? AccessId,
        string? Reason);

    /// <summary>
    /// Ledger state for one key, for reporting to an operator.
    /// </summary>
    public sealed record NzbKingKeyStatus(
        string KeyFingerprint,
        int EstimatedBalance,
        DateTime NextRefillAt,
        DateTime? LastSuccessfulUseAt,
        bool KeyDeleted);

    /// <summary>
    /// Meters access to NZBKing's API so its token allowance is never exhausted.
    ///
    /// NZBKing deletes a key whose balance reaches zero, and only a human solving a
    /// CAPTCHA can replace it. Every call to NZBKing must therefore acquire a lease
    /// first; refused means the request does not happen.
    /// </summary>
    public interface INzbKingTokenBudget
    {
        /// <summary>
        /// Attempts to reserve one token, recording the attempt either way.
        /// Never throws for an exhausted budget — inspect <see cref="NzbKingTokenLease.Granted"/>.
        /// </summary>
        Task<NzbKingTokenLease> TryAcquireAsync(
            string apiKey,
            NzbKingAccessPurpose purpose,
            string? query = null,
            CancellationToken ct = default);

        /// <summary>
        /// Reconciles a granted lease with what NZBKing actually returned. A 429 means the
        /// key has been deleted, which stops all further spending against it.
        /// </summary>
        Task ReportOutcomeAsync(
            NzbKingTokenLease lease,
            int httpStatus,
            CancellationToken ct = default);

        /// <summary>
        /// Current estimated state for a key, or null if it has never been used.
        /// </summary>
        Task<NzbKingKeyStatus?> GetStatusAsync(string apiKey, CancellationToken ct = default);
    }
}
