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
    /// Holds no arithmetic of its own: <see cref="NzbKingTokenPolicy"/> decides what one
    /// key can afford, <see cref="NzbKingKeyPool"/> decides which of several keys to
    /// charge, the repository applies the decision atomically, and this type joins them
    /// up and reports what happened.
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
            => (await AcquireAsync(apiKey, purpose, query, announce: true, ct)).Lease;

        public async Task<NzbKingPoolLease> TryAcquireFromPoolAsync(
            IReadOnlyList<string> apiKeys,
            NzbKingAccessPurpose purpose,
            string? query = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(apiKeys);

            var keysByFingerprint = FingerprintPool(apiKeys);
            if (keysByFingerprint.Count == 0)
            {
                return new NzbKingPoolLease(
                    new NzbKingTokenLease(
                        Granted: false,
                        KeyFingerprint: string.Empty,
                        BalanceAfter: 0,
                        NextRefillAt: DateTime.MinValue,
                        AccessId: null,
                        Reason: "No NZBKing API key is configured."),
                    ApiKey: null);
            }

            var members = await LoadMembersAsync(keysByFingerprint.Keys, ct);

            // Announced by the pool rather than by each attempt: trying a spent key before
            // a full one is the pool working, and a toast per attempt would report every
            // one of those as a failure the operator should act on.
            var refusals = new List<(NzbKingTokenLease Lease, bool KeyDeleted)>();
            foreach (var member in NzbKingKeyPool.SpendOrder(members))
            {
                ct.ThrowIfCancellationRequested();

                // The attempt's own verdict is kept, not the member's: the ordering read
                // the ledger a moment ago, and a key found dead on the way through is
                // dead whatever the ranking believed.
                var attempt = await AcquireAsync(
                    keysByFingerprint[member.Fingerprint], purpose, query, announce: false, ct);

                if (attempt.Lease.Granted)
                {
                    return new NzbKingPoolLease(attempt.Lease, keysByFingerprint[member.Fingerprint]);
                }

                refusals.Add(attempt);
            }

            return new NzbKingPoolLease(await AnnouncePoolRefusalAsync(refusals), ApiKey: null);
        }

        /// <summary>
        /// Spends one token against one key, or refuses.
        ///
        /// <paramref name="announce"/> is false when a pool is working through its keys:
        /// the refusal that matters is the pool's, once every key has declined.
        /// </summary>
        private async Task<(NzbKingTokenLease Lease, bool KeyDeleted)> AcquireAsync(
            string apiKey,
            NzbKingAccessPurpose purpose,
            string? query,
            bool announce,
            CancellationToken ct)
        {
            var fingerprint = NzbKingKeyFingerprint.Compute(apiKey);
            if (fingerprint == null)
            {
                return (new NzbKingTokenLease(
                    Granted: false,
                    KeyFingerprint: string.Empty,
                    BalanceAfter: 0,
                    NextRefillAt: DateTime.MinValue,
                    AccessId: null,
                    Reason: "No NZBKing API key is configured."), false);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var result = await _ledger.TrySpendAsync(fingerprint, purpose, query, now, ct);
            var nextRefill = NzbKingTokenPolicy.NextRefillAt(result.RefillAnchor);

            if (result.KeyDeleted)
            {
                _metrics.Increment("nzbking.tokens.denied");

                if (announce)
                {
                    await Notify("error", "NZBKing key no longer works",
                        "The key has been deleted by NZBKing. Request a new one and save it against the source.");
                }

                return (new NzbKingTokenLease(false, fingerprint, 0, nextRefill, result.AccessId,
                    "The NZBKing API key has been deleted. Request a new one and save it against this indexer."), true);
            }

            if (!result.Spent)
            {
                _metrics.Increment("nzbking.tokens.denied");
                _logger.LogWarning(
                    "NZBKing token budget refused a {Purpose} request; estimated balance {Balance} is at the reserve of {Reserve}. Next token at {NextRefill:u}",
                    purpose, result.BalanceAfter, NzbKingTokenPolicy.ReserveFloor, nextRefill);

                // A refusal blocked something the operator asked for, so it is worth
                // interrupting for - unlike the spends and refills either side of it.
                if (announce)
                {
                    await Notify("warning", "NZBKing allowance exhausted",
                        $"Held back to protect the key. One token returns every hour; the next at {nextRefill:HH:mm}.");
                }

                return (new NzbKingTokenLease(false, fingerprint, result.BalanceAfter, nextRefill, result.AccessId,
                    $"NZBKing token budget exhausted (estimated {result.BalanceAfter} left, reserve {NzbKingTokenPolicy.ReserveFloor}). Next token at {nextRefill:u}."), false);
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

            return (new NzbKingTokenLease(true, fingerprint, result.BalanceAfter, nextRefill, result.AccessId, null), false);
        }

        /// <summary>
        /// Reports a pool that could not pay, and returns the lease to hand back.
        ///
        /// The last refusal is kept rather than replaced so the ledger row it names
        /// survives: a refused attempt is a record that something was asked for and
        /// declined. Only the wording generalises, and only when there is more than one
        /// key to generalise about.
        /// </summary>
        private async Task<NzbKingTokenLease> AnnouncePoolRefusalAsync(
            IReadOnlyList<(NzbKingTokenLease Lease, bool KeyDeleted)> refusals)
        {
            var lastRefusal = refusals[^1].Lease;

            if (refusals.Count == 1)
            {
                if (refusals[0].KeyDeleted)
                {
                    await Notify("error", "NZBKing key no longer works",
                        "The key has been deleted by NZBKing. Request a new one and save it against the source.");
                }
                else
                {
                    await Notify("warning", "NZBKing allowance exhausted",
                        $"Held back to protect the key. One token returns every hour; the next at {lastRefusal.NextRefillAt:HH:mm}.");
                }

                return lastRefusal;
            }

            var liveRefusals = refusals.Where(refusal => !refusal.KeyDeleted).ToList();
            var deleted = refusals.Count - liveRefusals.Count;

            if (liveRefusals.Count == 0)
            {
                await Notify("error", "NZBKing keys no longer work",
                    $"All {refusals.Count} keys have been deleted by NZBKing. Request new ones and save them against the source.");

                return lastRefusal with
                {
                    Reason = $"All {refusals.Count} NZBKing API keys have been deleted. "
                        + "Request new ones and save them against this indexer."
                };
            }

            // When the pool can next pay, which is the earliest refill among the keys that
            // still refill at all - a deleted key's anchor keeps advancing and would
            // otherwise promise a token that is never coming.
            var nextRefill = liveRefusals.Min(refusal => refusal.Lease.NextRefillAt);
            var live = liveRefusals.Count;

            await Notify("warning", "NZBKing allowance exhausted",
                $"All {live} live keys are at their reserve. One token returns every hour per key below its cap; "
                + $"the next at {nextRefill:HH:mm}.");

            var reason = $"NZBKing token budget exhausted across {live} live "
                + $"{(live == 1 ? "key" : "keys")} (reserve {NzbKingTokenPolicy.ReserveFloor} each)"
                + (deleted > 0 ? $"; {deleted} deleted" : string.Empty)
                + ".";

            return lastRefusal with { Reason = reason, NextRefillAt = nextRefill };
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

            return StatusOf(state);
        }

        public async Task<IReadOnlyList<NzbKingKeyStatus>> GetPoolStatusAsync(
            IReadOnlyList<string> apiKeys,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(apiKeys);

            var keysByFingerprint = FingerprintPool(apiKeys);
            if (keysByFingerprint.Count == 0)
            {
                return [];
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var statuses = new Dictionary<string, NzbKingKeyStatus>(StringComparer.Ordinal);
            var members = new List<NzbKingPoolMember>();

            foreach (var fingerprint in keysByFingerprint.Keys)
            {
                ct.ThrowIfCancellationRequested();

                var state = await _ledger.GetByFingerprintAsync(fingerprint, ct);

                // A configured key the ledger has never seen is full: NZBKing issued it
                // with its whole allowance and nothing here has spent any of it. Reporting
                // it as absent would understate the pool by an entire key.
                var status = state == null
                    ? new NzbKingKeyStatus(
                        fingerprint,
                        NzbKingTokenPolicy.MaxTokens,
                        NzbKingTokenPolicy.NextRefillAt(now),
                        null,
                        false)
                    : StatusOf(state);

                statuses[fingerprint] = status;
                members.Add(new NzbKingPoolMember(fingerprint, status.EstimatedBalance, status.KeyDeleted));
            }

            return NzbKingKeyPool.SpendOrder(members)
                .Select(member => statuses[member.Fingerprint])
                .ToList();
        }

        private NzbKingKeyStatus StatusOf(NzbKingKeyState state)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var accrued = NzbKingTokenPolicy.Accrue(state.EstimatedBalance, state.LastRefillAt, now);

            return new NzbKingKeyStatus(
                state.KeyFingerprint,
                accrued.Balance,
                NzbKingTokenPolicy.NextRefillAt(accrued.RefillAnchor),
                state.LastSuccessfulUseAt,
                state.KeyDeletedAt.HasValue);
        }

        /// <summary>
        /// Fingerprint to key for a pool, which is also what collapses duplicates: two
        /// indexers configured with one key are one allowance, and offering it twice would
        /// have the pool believe otherwise.
        /// </summary>
        private static Dictionary<string, string> FingerprintPool(IReadOnlyList<string> apiKeys)
        {
            var keysByFingerprint = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var apiKey in apiKeys)
            {
                if (NzbKingKeyFingerprint.Compute(apiKey) is { Length: > 0 } fingerprint)
                {
                    keysByFingerprint.TryAdd(fingerprint, apiKey.Trim());
                }
            }

            return keysByFingerprint;
        }

        /// <summary>
        /// Current standing of every key, refills accrued, for the spend order to rank.
        /// </summary>
        private async Task<IReadOnlyList<NzbKingPoolMember>> LoadMembersAsync(
            IEnumerable<string> fingerprints,
            CancellationToken ct)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var members = new List<NzbKingPoolMember>();

            foreach (var fingerprint in fingerprints)
            {
                ct.ThrowIfCancellationRequested();

                var state = await _ledger.GetByFingerprintAsync(fingerprint, ct);
                if (state == null)
                {
                    // A key the ledger has never seen is full, so it ranks first and the
                    // next grab charges it. That is worth having: the keepalive can only
                    // touch keys the ledger knows about, so a key that is never spent is
                    // also a key that is never kept alive, and NZBKing deletes it after a
                    // month. Ranking it first means a configured key earns its row at the
                    // first opportunity rather than quietly lapsing.
                    members.Add(new NzbKingPoolMember(fingerprint, NzbKingTokenPolicy.MaxTokens, false));
                    continue;
                }

                var accrued = NzbKingTokenPolicy.Accrue(state.EstimatedBalance, state.LastRefillAt, now);
                members.Add(new NzbKingPoolMember(
                    fingerprint, accrued.Balance, state.KeyDeletedAt.HasValue));
            }

            return members;
        }
    }
}
