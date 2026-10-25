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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.NzbKing
{
    /// <summary>
    /// Issues one query against every NZBKing key that has been idle long enough to risk
    /// deletion for inactivity.
    ///
    /// The ledger stores only a fingerprint, never the key itself, so the raw key has to
    /// come back from the indexer that owns it. A key that is no longer configured simply
    /// stops being touched.
    /// </summary>
    public class NzbKingKeepaliveProcessor : INzbKingKeepaliveProcessor
    {
        private const string KeepaliveQuery = "audiobook";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<NzbKingKeepaliveProcessor> _logger;

        public NzbKingKeepaliveProcessor(
            IServiceScopeFactory scopeFactory,
            TimeProvider timeProvider,
            ILogger<NzbKingKeepaliveProcessor> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var ledger = scope.ServiceProvider.GetRequiredService<INzbKingLedgerRepository>();
            var indexers = scope.ServiceProvider.GetRequiredService<IIndexerRepository>();
            var client = scope.ServiceProvider.GetRequiredService<NzbKingApiClient>();

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var keysByFingerprint = await BuildKeyLookupAsync(indexers, cancellationToken);

            // Register before selecting. NZBKing deletes a key unused for a month, and the
            // selection below can only see keys the ledger has rows for - which, until
            // this, meant a configured key that no grab ever charged was never kept alive
            // and quietly died. A pool makes that the normal case rather than the unlucky
            // one: every key but the one the spend order picks first starts out unrecorded.
            foreach (var fingerprint in keysByFingerprint.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await ledger.EnsureKeyAsync(fingerprint, now, cancellationToken))
                {
                    _logger.LogInformation(
                        "Recorded NZBKing key {Fingerprint} so it can be kept alive; nothing has spent against it yet",
                        fingerprint);
                }
            }

            var due = await ledger.GetKeysDueForKeepaliveAsync(now, cancellationToken);
            if (due.Count == 0)
            {
                return;
            }

            foreach (var state in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!keysByFingerprint.TryGetValue(state.KeyFingerprint, out var apiKey))
                {
                    _logger.LogDebug(
                        "NZBKing key {Fingerprint} is due for keepalive but no indexer holds it; skipping",
                        state.KeyFingerprint);
                    continue;
                }

                _logger.LogInformation(
                    "Touching NZBKing key {Fingerprint}; unused since {LastUse:u}",
                    state.KeyFingerprint,
                    state.LastSuccessfulUseAt ?? state.CreatedAt);

                var result = await client.SearchAsync(
                    apiKey, KeepaliveQuery, NzbKingAccessPurpose.Keepalive, cancellationToken);

                if (!result.Succeeded)
                {
                    _logger.LogWarning(
                        "NZBKing keepalive for {Fingerprint} did not succeed: {Reason}",
                        state.KeyFingerprint,
                        result.Reason);
                }
            }
        }

        /// <summary>
        /// Every configured key, by the fingerprint the ledger knows it as.
        ///
        /// Every key, not the first one found: a key is deleted for a month's inactivity
        /// whether or not anything else in the pool is busy, and the spend order leaves
        /// the fullest keys untouched for exactly as long as the others can pay. A lookup
        /// holding one key would let the rest of the pool lapse.
        /// </summary>
        private static async Task<Dictionary<string, string>> BuildKeyLookupAsync(
            IIndexerRepository indexers,
            CancellationToken cancellationToken)
        {
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var apiKey in await NzbKingConfiguredKeys.CollectAsync(indexers, cancellationToken))
            {
                if (NzbKingKeyFingerprint.Compute(apiKey) is { Length: > 0 } fingerprint)
                {
                    lookup[fingerprint] = apiKey;
                }
            }

            return lookup;
        }
    }
}
