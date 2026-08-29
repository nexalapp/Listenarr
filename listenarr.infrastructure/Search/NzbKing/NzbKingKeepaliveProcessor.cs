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
    /// Issues one query against any NZBKing key that has been idle long enough to risk
    /// deletion for inactivity.
    ///
    /// The ledger stores only a fingerprint, never the key itself, so the raw key has to
    /// come back from the indexer that owns it. An indexer whose key is no longer
    /// configured simply stops being touched.
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
            var due = await ledger.GetKeysDueForKeepaliveAsync(now, cancellationToken);
            if (due.Count == 0)
            {
                return;
            }

            var keysByFingerprint = await BuildKeyLookupAsync(indexers, cancellationToken);

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

        private static async Task<Dictionary<string, string>> BuildKeyLookupAsync(
            IIndexerRepository indexers,
            CancellationToken cancellationToken)
        {
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var indexer in await indexers.GetAllAsync(cancellationToken))
            {
                var apiKey = NzbKingSettings.TryGetApiKey(indexer.AdditionalSettings);
                var fingerprint = NzbKingKeyFingerprint.Compute(apiKey);
                if (fingerprint != null && apiKey != null)
                {
                    lookup[fingerprint] = apiKey;
                }
            }

            return lookup;
        }
    }
}
