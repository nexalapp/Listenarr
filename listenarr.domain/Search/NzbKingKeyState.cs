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
using System.ComponentModel.DataAnnotations;

namespace Listenarr.Domain.Search
{
    /// <summary>
    /// Locally estimated state of a single NZBKing API key.
    ///
    /// NZBKing issues a key carrying 100 tokens, deducts one per query, and returns one
    /// per hour up to that cap. Reaching zero deletes the key, and recovering from that
    /// needs a human to solve a CAPTCHA, so the balance below is tracked defensively and
    /// spending stops well short of empty.
    ///
    /// There is no endpoint that reports the real balance, so this is an estimate that
    /// can drift from NZBKing's own count.
    /// </summary>
    public class NzbKingKeyState
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Stable hash of the API key. The key itself lives in the owning indexer's
        /// settings and is deliberately never copied here, so the ledger holds no secret.
        /// A newly issued key hashes differently and therefore starts a fresh balance.
        /// </summary>
        public string KeyFingerprint { get; set; } = string.Empty;

        public int EstimatedBalance { get; set; }

        /// <summary>
        /// Point from which unclaimed hourly refills accrue. Advanced by whole hours only,
        /// so a partial hour is carried forward rather than discarded.
        /// </summary>
        public DateTime LastRefillAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last query NZBKing actually accepted. Drives the keepalive: an unused key is
        /// deleted after a month.
        /// </summary>
        public DateTime? LastSuccessfulUseAt { get; set; }

        /// <summary>
        /// Set when NZBKing reports the key is gone (HTTP 429). Spending stops permanently
        /// for this fingerprint; a replacement key arrives as a different row.
        /// </summary>
        public DateTime? KeyDeletedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
