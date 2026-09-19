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
using Listenarr.Domain.Audiobooks.Chapters;
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
        public async Task SetChapterHealthAsync(
            IReadOnlyCollection<AudiobookFileChapterHealth> verdicts,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(verdicts);
            if (verdicts.Count == 0)
            {
                return;
            }

            var byId = verdicts.ToDictionary(verdict => verdict.FileId);
            var ids = byId.Keys.ToList();
            var files = await _db.AudiobookFiles
                .Where(file => ids.Contains(file.Id))
                .ToListAsync(ct);

            var changed = false;
            foreach (var file in files)
            {
                var verdict = byId[file.Id];
                if (file.ChapterHealth == verdict.Health
                    && file.ChapterReason == verdict.Reason
                    && file.ChapterCount == verdict.ChapterCount
                    && file.ChapterRepairable == verdict.Repairable)
                {
                    continue;
                }

                file.ChapterHealth = verdict.Health;
                file.ChapterReason = verdict.Reason;
                file.ChapterCount = verdict.ChapterCount;
                file.ChapterRepairable = verdict.Repairable;
                changed = true;
            }

            if (changed)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        public async Task SetChapterPlanAsync(int fileId, string planJson, string planKey, bool repairable, DateTime plannedAtUtc, CancellationToken ct = default)
        {
            var file = await _db.AudiobookFiles.FirstOrDefaultAsync(candidate => candidate.Id == fileId, ct);
            if (file == null)
            {
                return;
            }

            file.ChapterPlanJson = planJson;
            file.ChapterPlanKey = planKey;
            file.ChapterPlannedAt = plannedAtUtc;
            // A plan is the definitive word on repairability; the cheap guess gives way.
            file.ChapterRepairable = repairable;
            await _db.SaveChangesAsync(ct);
        }

        public async Task<Dictionary<int, AudiobookChapterSummary>> GetWorstChapterHealthByAudiobookIdAsync(CancellationToken ct = default)
        {
            var rows = await _db.AudiobookFiles
                .AsNoTracking()
                .Where(file => file.ChapterHealth != ChapterHealth.Unknown)
                .Select(file => new { file.AudiobookId, file.ChapterHealth, file.ChapterRepairable })
                .ToListAsync(ct);

            // The worst verdict wins; among files with that verdict, one that cannot be
            // repaired is the one to report — red must not be hidden behind amber.
            var worst = new Dictionary<int, AudiobookChapterSummary>();
            foreach (var row in rows)
            {
                var rank = ChapterHealthSeverity.Rank(row.ChapterHealth);
                if (!worst.TryGetValue(row.AudiobookId, out var current)
                    || rank > ChapterHealthSeverity.Rank(current.Health)
                    || (rank == ChapterHealthSeverity.Rank(current.Health) && current.Repairable && !row.ChapterRepairable))
                {
                    worst[row.AudiobookId] = new AudiobookChapterSummary(row.ChapterHealth, row.ChapterRepairable);
                }
            }

            return worst;
        }
    }
}
