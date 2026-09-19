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
    /// The not-found flag: what a scan sets when a file is not at its path, what the
    /// next scan clears, and what an operator removes rows by. A scan never deletes.
    /// </summary>
    public partial class EfAudiobookFileRepository
    {
        public async Task<IReadOnlyList<int>> MarkNotFoundAsync(IReadOnlyCollection<int> fileIds, DateTime whenUtc, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(fileIds);
            if (fileIds.Count == 0)
            {
                return [];
            }

            var ids = fileIds.ToList();
            var files = await _db.AudiobookFiles
                .Where(file => ids.Contains(file.Id) && file.NotFoundSinceUtc == null)
                .ToListAsync(ct);
            foreach (var file in files)
            {
                // The first time it went missing is the useful time; a flag that is re-set
                // on every scan would say nothing.
                file.NotFoundSinceUtc = whenUtc;
            }

            if (files.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            return files.Select(file => file.Id).ToList();
        }

        public async Task ClearNotFoundAsync(IReadOnlyCollection<int> fileIds, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(fileIds);
            if (fileIds.Count == 0)
            {
                return;
            }

            var ids = fileIds.ToList();
            var files = await _db.AudiobookFiles
                .Where(file => ids.Contains(file.Id) && file.NotFoundSinceUtc != null)
                .ToListAsync(ct);
            foreach (var file in files)
            {
                file.NotFoundSinceUtc = null;
            }

            if (files.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        public async Task<IReadOnlyList<AudiobookFileRemoved>> DeleteNotFoundAsync(int audiobookId, CancellationToken ct = default)
        {
            var files = await _db.AudiobookFiles
                .Where(file => file.AudiobookId == audiobookId && file.NotFoundSinceUtc != null)
                .ToListAsync(ct);
            if (files.Count == 0)
            {
                return [];
            }

            _db.AudiobookFiles.RemoveRange(files);
            await _db.SaveChangesAsync(ct);
            return files.Select(file => new AudiobookFileRemoved(file.Id, file.Path)).ToList();
        }

        public async Task<Dictionary<int, int>> GetNotFoundCountsByAudiobookIdAsync(CancellationToken ct = default)
        {
            return await _db.AudiobookFiles
                .AsNoTracking()
                .Where(file => file.NotFoundSinceUtc != null)
                .GroupBy(file => file.AudiobookId)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(entry => entry.Key, entry => entry.Count, ct);
        }
    }
}
