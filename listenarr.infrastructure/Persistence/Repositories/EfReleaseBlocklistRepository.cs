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
    public sealed class EfReleaseBlocklistRepository(ListenArrDbContext db) : IReleaseBlocklistRepository
    {
        public async Task<List<BlockedRelease>> GetForAudiobookAsync(
            int audiobookId,
            CancellationToken ct = default) =>
            await db.BlockedReleases
                .AsNoTracking()
                .Where(blocked => blocked.AudiobookId == audiobookId)
                .ToListAsync(ct);

        public async Task<BlockedRelease> AddAsync(BlockedRelease entry, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(entry);

            // Two grabs of one release can be in flight at once - a failure that starts a
            // search, and the search's own grab arriving before the first row is written.
            // Both end at the same block, and that is one block.
            var existing = await db.BlockedReleases.FirstOrDefaultAsync(
                blocked => blocked.AudiobookId == entry.AudiobookId &&
                    blocked.Fingerprint == entry.Fingerprint,
                ct);
            if (existing != null)
            {
                return existing;
            }

            db.BlockedReleases.Add(entry);
            await db.SaveChangesAsync(ct);
            db.Entry(entry).State = EntityState.Detached;
            return entry;
        }

        public async Task<List<BlockedRelease>> GetRecentAsync(int limit, CancellationToken ct = default) =>
            await db.BlockedReleases
                .AsNoTracking()
                .OrderByDescending(blocked => blocked.BlockedAt)
                .Take(Math.Clamp(limit, 1, 500))
                .ToListAsync(ct);

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var existing = await db.BlockedReleases.FirstOrDefaultAsync(blocked => blocked.Id == id, ct);
            if (existing == null)
            {
                return false;
            }

            db.BlockedReleases.Remove(existing);
            await db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<int> DeleteForAudiobookAsync(int audiobookId, CancellationToken ct = default)
        {
            var existing = await db.BlockedReleases
                .Where(blocked => blocked.AudiobookId == audiobookId)
                .ToListAsync(ct);
            if (existing.Count == 0)
            {
                return 0;
            }

            db.BlockedReleases.RemoveRange(existing);
            await db.SaveChangesAsync(ct);
            return existing.Count;
        }
    }
}
