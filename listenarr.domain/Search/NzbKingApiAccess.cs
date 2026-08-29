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
    /// Why a caller wanted to reach NZBKing.
    /// </summary>
    public enum NzbKingAccessPurpose
    {
        /// <summary>Resolving a release to an NZB because a download was requested.</summary>
        Grab,

        /// <summary>Periodic touch so an idle key is not deleted for inactivity.</summary>
        Keepalive,

        /// <summary>Operator-initiated connection test.</summary>
        Test
    }

    /// <summary>
    /// What came of an access attempt.
    /// </summary>
    public enum NzbKingAccessOutcome
    {
        /// <summary>A token was spent and the request was issued.</summary>
        Spent,

        /// <summary>Refused locally to protect the reserve; no request was issued.</summary>
        DeniedByBudget,

        /// <summary>A token was spent but the request failed.</summary>
        Failed,

        /// <summary>NZBKing reported the key no longer exists.</summary>
        KeyDeleted
    }

    /// <summary>
    /// One attempted use of an NZBKing API key, successful or not.
    ///
    /// Refused attempts are recorded too: when the budget runs dry the interesting
    /// question is what consumed it, and a log of only the successes cannot answer that.
    /// </summary>
    public class NzbKingApiAccess
    {
        [Key]
        public int Id { get; set; }

        public string KeyFingerprint { get; set; } = string.Empty;

        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

        public NzbKingAccessPurpose Purpose { get; set; }

        public NzbKingAccessOutcome Outcome { get; set; }

        /// <summary>Search term the caller wanted, kept for debugging and de-duplication.</summary>
        public string? Query { get; set; }

        public int? HttpStatus { get; set; }

        /// <summary>Estimated balance once this attempt was accounted for.</summary>
        public int BalanceAfter { get; set; }
    }
}
