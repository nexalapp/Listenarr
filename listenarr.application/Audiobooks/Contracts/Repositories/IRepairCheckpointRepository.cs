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

namespace Listenarr.Application.Audiobooks.Contracts.Repositories
{
    /// <summary>
    /// The per-file checkpoints a repair leaves behind so a restart redoes one chunk,
    /// not a day of model time.
    /// </summary>
    public interface IRepairCheckpointRepository
    {
        Task<IReadOnlyList<RepairFileCheckpoint>> GetForJobAsync(Guid jobId, CancellationToken ct = default);

        /// <summary>Record a finished intermediate; replaces an existing row for the same (job, file).</summary>
        Task UpsertAsync(RepairFileCheckpoint checkpoint, CancellationToken ct = default);

        Task RemoveAsync(Guid checkpointId, CancellationToken ct = default);

        /// <summary>Clear a job's rows, when its scratch directory is swept.</summary>
        Task RemoveForJobAsync(Guid jobId, CancellationToken ct = default);
    }
}
