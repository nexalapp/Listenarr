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
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// The names the library already uses, which is the dictionary a name heard in the
    /// audio is spelled against.
    /// </summary>
    public partial class AudiobookRepository
    {
        public async Task<IReadOnlyList<string>> GetKnownNarratorsAsync(CancellationToken ct = default)
        {
            // Narrators are a serialised list on the row, so the distinct set is built
            // here rather than asked of the database. The library is a few thousand rows
            // and this answers a question asked once per audit.
            var lists = await _db.Audiobooks
                .AsNoTracking()
                .Where(a => a.Narrators != null)
                .Select(a => a.Narrators!)
                .ToListAsync(ct);

            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in lists.SelectMany(list => list))
            {
                var trimmed = name?.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    // First spelling wins, so the set is stable between calls.
                    names.TryAdd(trimmed, trimmed);
                }
            }

            return names.Values.ToList();
        }
    }
}
