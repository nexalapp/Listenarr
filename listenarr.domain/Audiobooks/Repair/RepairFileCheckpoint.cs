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

namespace Listenarr.Domain.Audiobooks.Repair
{
    /// <summary>
    /// One source file a repair has finished enhancing: which file, what it looked like,
    /// where the intermediate landed, and under which enhancer.
    ///
    /// A row per file rather than progress on the job, because the intermediate is the
    /// thing being protected: it represents hours of model time, and a restart must be
    /// able to prove "this FLAC really is the enhanced form of that source under this
    /// model" before trusting it. Any mismatch — source changed, intermediate truncated,
    /// enhancer upgraded — and the row is discarded and the file redone.
    /// </summary>
    public class RepairFileCheckpoint
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>The repair job the intermediate belongs to. Unique with <see cref="AudiobookFileId"/>.</summary>
        public Guid JobId { get; set; }

        public int AudiobookFileId { get; set; }

        // What the source looked like when it was enhanced.
        [MaxLength(2000)]
        public string SourcePath { get; set; } = string.Empty;
        public long SourceLength { get; set; }
        public DateTime SourceLastWriteUtc { get; set; }

        // The intermediate, identified the same way a verified encode is.
        [MaxLength(2000)]
        public string EnhancedPath { get; set; } = string.Empty;
        public long EnhancedLength { get; set; }

        [MaxLength(64)]
        public string EnhancerVersion { get; set; } = string.Empty;

        public DateTime CompletedAtUtc { get; set; }
    }
}
