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
    /// One press of abook.link's thank button, recorded because it is the only thing
    /// Listenarr does on that forum that other members can see. Thanking is how a post
    /// gives up the payload behind it, so it is not optional - but it is public, it is
    /// irreversible, and several unrelated paths reach it, so the rate it happens at is
    /// nobody's to know unless it is written down.
    ///
    /// The row survives restarts on purpose. An in-memory counter resets on every deploy,
    /// and this fork deploys several times a day, so a budget that lived in memory would
    /// be no budget at all.
    /// </summary>
    public class AbookThank
    {
        [Key]
        public int Id { get; set; }

        public int TopicId { get; set; }

        public DateTime ThankedAt { get; set; } = DateTime.UtcNow;

        /// <summary>False when the forum refused it; those are what open the breaker.</summary>
        public bool Succeeded { get; set; }

        [MaxLength(256)]
        public string? Detail { get; set; }
    }
}
