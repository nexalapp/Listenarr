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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.NzbKing
{
    /// <summary>
    /// Gate in front of NZBKing's API.
    ///
    /// Holds no arithmetic of its own: <see cref="NzbKingTokenPolicy"/> decides, the
    /// repository applies the decision atomically, and this type joins them up and
    /// reports what happened.
    /// </summary>
    public class NzbKingTokenBudget : INzbKingTokenBudget
    {
        /// <summary>
        /// Balance at which the allowance is worth mentioning. Announced once, when it is
        /// crossed - repeating it on every spend below the line is how a warning becomes
        /// wallpaper.
        /// </summary>
        private const int LowBalanceThreshold = 20;

        private readonly INzbKingLedgerRepository _ledger;
        private readonly IAppMetricsService _metrics;
        private readonly IToastService _toasts;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<NzbKingTokenBudget> _logger;

        public NzbKingTokenBudget(
            INzbKingLedgerRepository ledger,
            IAppMetricsService metrics,
            IToastService toasts,
            TimeProvider timeProvider,
            ILogger<NzbKingTokenBudget> logger)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _toasts = toasts ?? throw new ArgumentNullException(nameof(toasts));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<NzbKingTokenLease> TryAcquireAsync(
            string apiKey,
            NzbKingAccessPurpose purpose,
            string? query = null,
            CancellationToken ct = default)
        {
            var fingerprint = NzbKingKeyFingerprint.Compute(apiKey);
            if (fingerprint == null)
            {
                return new NzbKingTokenLease(
                    Granted: false,
                    KeyFingerprint: string.Empty,
                    BalanceAfter: 0,
                    NextRefillAt: DateTime.MinValue,
                    AccessId: null,
                    Reason: "No NZBKing API key is configured.");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var result = await _ledger.TrySpendAsync(fingerprint, purpose, query, now, ct);
            var nextRefill = NzbKingTokenPolicy.NextRefillAt(result.RefillAnchor);

            if (result.KeyDeleted)
            {
                _metrics.Increment("nzbking.tokens.denied");

                await Notify("error", "NZBKing key no longer works",
                    "The key has been deleted by NZBKing. Request a new one and save it against the source.");

                return new NzbKingTokenLease(false, fingerprint, 0, nextRefill, result.AccessId,
                    "The NZBKing API key has been deleted. Request a new one and save it against this indexer.");
            }

            if (!result.Spent)
            {
                _metrics.Increment("nzbking.tokens.denied");
                _logger.LogWarning(
                    "NZBKing token budget refused a {Purpose} request; estimated balance {Balance} is at the reserve of {Reserve}. Next token at {NextRefill:u}",
                    purpose, result.BalanceAfter, NzbKingTokenPolicy.ReserveFloor, nextRefill);

                // A refusal blocked something the operator asked for, so it is worth
                // interrupting for - unlike the spends and refills either side of it.
                await Notify("warning", "NZBKing allowance exhausted",
                    $"Held back to protect the key. One token returns every hour; the next at {nextRefill:HH:mm}.");

                return new NzbKingTokenLease(false, fingerprint, result.BalanceAfter, nextRefill, result.AccessId,
                    $"NZBKing token budget exhausted (estimated {result.BalanceAfter} left, reserve {NzbKingTokenPolicy.ReserveFloor}). Next token at {nextRefill:u}.");
            }

            _metrics.Increment("nzbking.tokens.spent");
            _metrics.Gauge("nzbking.tokens.remaining", result.BalanceAfter);

            // Only on the way past the line, and only to the activity bell. A spend is
            // already visible in the grab that caused it, and an hourly refill is not news.
            if (result.BalanceAfter < LowBalanceThreshold
                && result.BalanceAfter + 1 >= LowBalanceThreshold)
            {
                await NotifyQuietly("NZBKing allowance running low",
                    $"About {result.BalanceAfter} of {NzbKingTokenPolicy.MaxTokens} tokens left. "
                    + "One returns every hour; grabs stop before the key can be deleted.");
            }

            return new NzbKingTokenLease(true, fingerprint, result.BalanceAfter, nextRefill, result.AccessId, null);
        }

        public async Task ReportOutcomeAsync(
            NzbKingTokenLease lease,
            int httpStatus,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(lease);
            if (!lease.Granted || lease.AccessId is not { } accessId)
            {
                return;
            }

            var outcome = httpStatus switch
            {
                429 => NzbKingAccessOutcome.KeyDeleted,
                >= 200 and < 300 => NzbKingAccessOutcome.Spent,
                _ => NzbKingAccessOutcome.Failed
            };

            if (outcome == NzbKingAccessOutcome.KeyDeleted)
            {
                _logger.LogError(
                    "NZBKing returned 429; the API key is gone and must be replaced by requesting a new one.");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await _ledger.RecordOutcomeAsync(accessId, lease.KeyFingerprint, outcome, httpStatus, now, ct);
        }

        /// <summary>
        /// Toasts must never take down the thing they report on: a broadcast failure is
        /// logged and swallowed rather than turning a working grab into a failed one.
        /// </summary>
        private async Task Notify(string level, string title, string message)
        {
            try
            {
                await _toasts.PublishToastAsync(level, title, message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not publish an NZBKing budget toast");
            }
        }

        private async Task NotifyQuietly(string title, string message)
        {
            try
            {
                await _toasts.PublishNotificationAsync(title, message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not publish an NZBKing budget notification");
            }
        }

        public async Task<NzbKingKeyStatus?> GetStatusAsync(string apiKey, CancellationToken ct = default)
        {
            var fingerprint = NzbKingKeyFingerprint.Compute(apiKey);
            if (fingerprint == null)
            {
                return null;
            }

            var state = await _ledger.GetByFingerprintAsync(fingerprint, ct);
            if (state == null)
            {
                return null;
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var accrued = NzbKingTokenPolicy.Accrue(state.EstimatedBalance, state.LastRefillAt, now);

            return new NzbKingKeyStatus(
                state.KeyFingerprint,
                accrued.Balance,
                NzbKingTokenPolicy.NextRefillAt(accrued.RefillAnchor),
                state.LastSuccessfulUseAt,
                state.KeyDeletedAt.HasValue);
        }
    }
}
