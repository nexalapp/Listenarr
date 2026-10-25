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

namespace Listenarr.Infrastructure.Search.NzbKing
{
    /// <summary>
    /// Collects every NZBKing key the installation has configured.
    ///
    /// The keys live on indexers, and more than one indexer may carry one. Everything that
    /// meters, reports on or keeps keys alive needs the same full set, so it is gathered in
    /// one place: a caller that reads only the first key it finds silently spends, shows
    /// and protects a fraction of the pool, which is the bug this exists to prevent.
    /// </summary>
    internal static class NzbKingConfiguredKeys
    {
        /// <summary>
        /// Every configured key, de-duplicated, in indexer order.
        /// </summary>
        public static async Task<IReadOnlyList<string>> CollectAsync(
            IIndexerRepository indexers,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(indexers);

            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var indexer in await indexers.GetAllAsync(ct))
            {
                foreach (var key in NzbKingSettings.TryGetApiKeys(indexer.AdditionalSettings))
                {
                    if (seen.Add(key))
                    {
                        keys.Add(key);
                    }
                }
            }

            return keys;
        }
    }
}
