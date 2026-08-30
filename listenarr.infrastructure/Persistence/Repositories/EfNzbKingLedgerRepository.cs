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
using Listenarr.Application.Search.NzbKing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    public class EfNzbKingLedgerRepository : INzbKingLedgerRepository
    {
        private readonly ListenArrDbContext _db;

        public EfNzbKingLedgerRepository(ListenArrDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<NzbKingKeyState?> GetByFingerprintAsync(string fingerprint, CancellationToken ct = default)
        {
            return await _db.NzbKingKeyStates
                .AsNoTracking()
                .FirstOrDefaultAsync(state => state.KeyFingerprint == fingerprint, ct);
        }

        public async Task<NzbKingSpendResult> TrySpendAsync(
            string fingerprint,
            NzbKingAccessPurpose purpose,
            string? query,
            DateTime now,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

            // Accrue, decide and deduct inside one transaction. Two grabs racing here would
            // otherwise both read the pre-deduction balance, both consider themselves within
            // the reserve, and together spend past it — which is exactly how the key gets
            // deleted. SQLite serialises writers, so the transaction is sufficient.
            //
            // The in-memory provider used by parts of the test harness supports neither
            // transactions nor concurrent writers, so there is nothing to guard there.
            //
            // Note this is defence in depth rather than a proven guarantee: EF opens a
            // deferred transaction, so a read-then-write interleaving could in principle
            // still lose an update. The consequence is bounded — the estimate drifts one
            // token high, which NzbKingTokenPolicy.ReserveFloor exists to absorb. Making
            // it airtight would mean a compare-and-swap update, which the in-memory
            // provider cannot express.
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(ct)
                : null;

            var state = await _db.NzbKingKeyStates
                .FirstOrDefaultAsync(s => s.KeyFingerprint == fingerprint, ct);

            if (state == null)
            {
                // First sighting of this key. NZBKing issues 100 tokens with a new key, so
                // that is the only defensible starting estimate — a key that was not freshly
                // issued will read high until a 429 corrects us.
                state = new NzbKingKeyState
                {
                    KeyFingerprint = fingerprint,
                    EstimatedBalance = NzbKingTokenPolicy.MaxTokens,
                    LastRefillAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.NzbKingKeyStates.Add(state);
            }

            if (state.KeyDeletedAt.HasValue)
            {
                var refusal = await LogAsync(
                    fingerprint, purpose, query, NzbKingAccessOutcome.KeyDeleted, null, 0, now, ct);
                await CommitIfPresentAsync(transaction, ct);
                return new NzbKingSpendResult(false, 0, state.LastRefillAt, refusal, KeyDeleted: true);
            }

            var accrued = NzbKingTokenPolicy.Accrue(state.EstimatedBalance, state.LastRefillAt, now);
            state.EstimatedBalance = accrued.Balance;
            state.LastRefillAt = accrued.RefillAnchor;

            if (!NzbKingTokenPolicy.CanSpend(accrued.Balance))
            {
                state.UpdatedAt = now;
                var denialId = await LogAsync(
                    fingerprint, purpose, query, NzbKingAccessOutcome.DeniedByBudget, null, accrued.Balance, now, ct);
                await CommitIfPresentAsync(transaction, ct);
                return new NzbKingSpendResult(false, accrued.Balance, accrued.RefillAnchor, denialId, KeyDeleted: false);
            }

            state.EstimatedBalance = accrued.Balance - 1;
            state.UpdatedAt = now;

            var accessId = await LogAsync(
                fingerprint, purpose, query, NzbKingAccessOutcome.Spent, null, state.EstimatedBalance, now, ct);
            await CommitIfPresentAsync(transaction, ct);

            return new NzbKingSpendResult(true, state.EstimatedBalance, accrued.RefillAnchor, accessId, KeyDeleted: false);
        }

        public async Task RecordOutcomeAsync(
            int accessId,
            string fingerprint,
            NzbKingAccessOutcome outcome,
            int httpStatus,
            DateTime now,
            CancellationToken ct = default)
        {
            var access = await _db.NzbKingApiAccesses.FindAsync(new object[] { accessId }, ct);
            if (access != null)
            {
                access.Outcome = outcome;
                access.HttpStatus = httpStatus;
            }

            var state = await _db.NzbKingKeyStates
                .FirstOrDefaultAsync(s => s.KeyFingerprint == fingerprint, ct);

            if (state != null)
            {
                if (outcome == NzbKingAccessOutcome.KeyDeleted)
                {
                    // NZBKing only reports this once the key is already gone; nothing is
                    // recoverable except issuing a new one, so stop spending against it.
                    state.KeyDeletedAt = now;
                    state.EstimatedBalance = 0;
                }
                else if (outcome == NzbKingAccessOutcome.Spent)
                {
                    state.LastSuccessfulUseAt = now;
                }

                state.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task<List<NzbKingKeyState>> GetKeysDueForKeepaliveAsync(DateTime now, CancellationToken ct = default)
        {
            var cutoff = now - NzbKingTokenPolicy.KeepaliveAfter;

            return await _db.NzbKingKeyStates
                .AsNoTracking()
                .Where(state => state.KeyDeletedAt == null
                    && (state.LastSuccessfulUseAt ?? state.CreatedAt) <= cutoff)
                .ToListAsync(ct);
        }

        public async Task<List<NzbKingApiAccess>> GetRecentAccessAsync(
            string fingerprint,
            int limit,
            CancellationToken ct = default)
        {
            if (limit <= 0)
            {
                return [];
            }

            return await _db.NzbKingApiAccesses
                .AsNoTracking()
                .Where(access => access.KeyFingerprint == fingerprint)
                .OrderByDescending(access => access.AttemptedAt)
                .ThenByDescending(access => access.Id)
                .Take(limit)
                .ToListAsync(ct);
        }

        private async Task<int> LogAsync(
            string fingerprint,
            NzbKingAccessPurpose purpose,
            string? query,
            NzbKingAccessOutcome outcome,
            int? httpStatus,
            int balanceAfter,
            DateTime now,
            CancellationToken ct)
        {
            var access = new NzbKingApiAccess
            {
                KeyFingerprint = fingerprint,
                AttemptedAt = now,
                Purpose = purpose,
                Outcome = outcome,
                Query = Truncate(query),
                HttpStatus = httpStatus,
                BalanceAfter = balanceAfter
            };

            _db.NzbKingApiAccesses.Add(access);
            await _db.SaveChangesAsync(ct);
            return access.Id;
        }

        private static async Task CommitIfPresentAsync(IDbContextTransaction? transaction, CancellationToken ct)
        {
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }
        }

        private static string? Truncate(string? query)
            => query != null && query.Length > 512 ? query[..512] : query;
    }
}
