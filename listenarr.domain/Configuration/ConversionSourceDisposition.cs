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

namespace Listenarr.Domain.Configuration
{
    /// <summary>
    /// What happens to the source MP3s after a conversion has been verified.
    ///
    /// Nothing here runs until the M4B has been read back and its chapters and duration
    /// confirmed, so a failed conversion always leaves the sources exactly as they were.
    /// </summary>
    public enum ConversionSourceDisposition
    {
        /// <summary>
        /// Move the sources out of the library to the configured archive path. The default:
        /// it reclaims the library folder without making a bad conversion unrecoverable.
        /// </summary>
        Archive,

        /// <summary>
        /// Leave the sources on disk and drop them from the library record. Nothing is
        /// moved or deleted; the operator reclaims the space themselves.
        /// </summary>
        Keep,

        /// <summary>
        /// Delete the sources. Reclaims space immediately and cannot be undone, so a
        /// conversion problem noticed later has nothing to fall back to.
        /// </summary>
        Delete
    }
}
