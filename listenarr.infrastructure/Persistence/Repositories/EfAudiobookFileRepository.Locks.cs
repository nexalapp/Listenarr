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
    /// The writes that record what may not be changed about a file: which of its tags no
    /// write may touch, and whether organizing may move or rename it at all.
    /// </summary>
    public partial class EfAudiobookFileRepository
    {
        public async Task<Dictionary<int, bool>> SetPathLockedAsync(
            IReadOnlyCollection<int> fileIds,
            bool locked,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(fileIds);

            var resulting = new Dictionary<int, bool>();
            if (fileIds.Count == 0)
            {
                return resulting;
            }

            var ids = fileIds.Distinct().ToList();
            var files = await _db.AudiobookFiles
                .Where(file => ids.Contains(file.Id))
                .ToListAsync(ct);

            foreach (var file in files)
            {
                file.PathLocked = locked;
                resulting[file.Id] = locked;
            }

            if (resulting.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            return resulting;
        }

        public async Task<Dictionary<int, List<string>>> SetLockedTagsAsync(
            IReadOnlyCollection<int> fileIds,
            IReadOnlyCollection<string> tags,
            bool locked,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(fileIds);
            ArgumentNullException.ThrowIfNull(tags);

            var resulting = new Dictionary<int, List<string>>();
            if (fileIds.Count == 0)
            {
                return resulting;
            }

            var ids = fileIds.Distinct().ToList();
            var files = await _db.AudiobookFiles
                .Where(file => ids.Contains(file.Id))
                .ToListAsync(ct);

            foreach (var file in files)
            {
                var current = new HashSet<string>(
                    file.LockedTags ?? [],
                    StringComparer.OrdinalIgnoreCase);

                foreach (var tag in tags)
                {
                    if (locked)
                    {
                        current.Add(tag);
                    }
                    else
                    {
                        current.Remove(tag);
                    }
                }

                // Normalised on the way in, so what a later read compares against is the
                // catalog's casing and order however the request happened to be phrased.
                var normalized = TagCatalog.NormalizeLocks(current);

                // An empty set is stored as null rather than as "[]": a file nobody has
                // locked anything on should be indistinguishable from one that predates
                // the column.
                file.LockedTags = normalized.Count == 0 ? null : normalized;
                resulting[file.Id] = normalized;
            }

            if (resulting.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            return resulting;
        }
    }
}
