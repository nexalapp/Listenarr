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
using System.Security.Cryptography;
using System.Text;

namespace Listenarr.Application.Search.NzbKing
{
    /// <summary>
    /// Derives the ledger's identifier for an NZBKing API key.
    ///
    /// The budget belongs to the key, not to whichever indexer happens to reference it,
    /// so the ledger is keyed by this hash. Two indexers configured with one key then
    /// share a single budget instead of each believing it has a full allowance, and a
    /// freshly issued key hashes differently and starts a clean balance.
    ///
    /// Hashing also keeps the secret out of the ledger entirely.
    /// </summary>
    public static class NzbKingKeyFingerprint
    {
        /// <summary>
        /// Stable fingerprint for <paramref name="apiKey"/>, or null when there is no key.
        /// </summary>
        public static string? Compute(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return null;
            }

            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey.Trim()));
            return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
        }
    }
}
