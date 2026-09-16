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

namespace Listenarr.Domain.Audiobooks
{
    /// <summary>
    /// A suggested book the user said no to, so the Suggested page stops offering it.
    ///
    /// Keyed by ASIN when the catalog had one, otherwise by the normalised title and
    /// first author - the same identity the page uses to recognise a held book, so a
    /// dismissed translation or regional retitle stays dismissed across refreshes.
    /// </summary>
    public class SuggestionDismissal
    {
        [Key]
        public int Id { get; set; }

        /// <summary><c>asin:B0…</c> or <c>key:{title}|{author}</c>.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>What was dismissed, for the list that lets it be undone.</summary>
        public string Title { get; set; } = string.Empty;

        public string? Author { get; set; }

        public DateTime DismissedAt { get; set; } = DateTime.UtcNow;
    }
}
