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
    /// What one library file's embedded tags were the last time it was probed, so the
    /// tag table survives a restart without re-reading every file.
    ///
    /// Keyed by the file's full path - the path is the identity - and trusted only while
    /// the file's size and modification time still match. A rewritten file misses and is
    /// probed again; nothing has to invalidate it.
    /// </summary>
    public class LibraryTagCacheEntry
    {
        [Key]
        public string Path { get; set; } = string.Empty;

        public long Length { get; set; }

        public DateTime LastWriteUtc { get; set; }

        /// <summary>The probe result, serialised; the table never queries inside it.</summary>
        public string TagsJson { get; set; } = string.Empty;

        public DateTime ReadAt { get; set; } = DateTime.UtcNow;
    }
}
