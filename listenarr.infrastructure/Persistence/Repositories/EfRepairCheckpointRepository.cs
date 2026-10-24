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
using Listenarr.Domain.Audiobooks.Repair;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    public sealed class EfRepairCheckpointRepository(ListenArrDbContext db) : IRepairCheckpointRepository
    {
        public async Task<IReadOnlyList<RepairFileCheckpoint>> GetForJobAsync(Guid jobId, CancellationToken ct = default) =>
            await db.RepairFileCheckpoints
                .AsNoTracking()
                .Where(checkpoint => checkpoint.JobId == jobId)
                .ToListAsync(ct);

        public async Task UpsertAsync(RepairFileCheckpoint checkpoint, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(checkpoint);

            var existing = await db.RepairFileCheckpoints
                .FirstOrDefaultAsync(
                    row => row.JobId == checkpoint.JobId && row.AudiobookFileId == checkpoint.AudiobookFileId,
                    ct);
            if (existing != null)
            {
                db.RepairFileCheckpoints.Remove(existing);
            }

            db.RepairFileCheckpoints.Add(checkpoint);
            await db.SaveChangesAsync(ct);
        }

        public async Task RemoveAsync(Guid checkpointId, CancellationToken ct = default) =>
            await db.RepairFileCheckpoints
                .Where(checkpoint => checkpoint.Id == checkpointId)
                .ExecuteDeleteAsync(ct);

        public async Task RemoveForJobAsync(Guid jobId, CancellationToken ct = default) =>
            await db.RepairFileCheckpoints
                .Where(checkpoint => checkpoint.JobId == jobId)
                .ExecuteDeleteAsync(ct);
    }
}
