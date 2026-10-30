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

namespace Listenarr.Domain.Downloads
{
    /// <summary>
    /// What went wrong with a download, in the terms the next decision needs.
    ///
    /// "Failed" on its own says nothing anyone can act on. A release too few of whose
    /// articles still exist, one that arrived damaged, and one the client threw away
    /// before fetching a byte all read the same under that word, and only two of the
    /// three are the release's own fault. The reason decides whether the release is
    /// blocked, whether the client's record of it has to go, and whether another is
    /// worth looking for - and it is what the Activity row says instead of "Failed".
    ///
    /// Stored as a number, so a member may be appended without touching a stored row.
    /// </summary>
    public enum DownloadFailureReason
    {
        /// <summary>Nothing recognisable in what the client said; its own words are all there is.</summary>
        Unknown = 0,

        /// <summary>
        /// Too little of the release survives on the server to rebuild it. Nothing about
        /// the post will improve, so the same release is not worth another grab.
        /// </summary>
        Health = 1,

        /// <summary>
        /// It came down, and verification or unpacking could not make whole files of it.
        /// </summary>
        Damaged = 2,

        /// <summary>
        /// The archive is encrypted and the password did not open it - usually a sign the
        /// NZB is not the release the password came with.
        /// </summary>
        Password = 3,

        /// <summary>
        /// The client discarded the grab as a copy of something already in its own
        /// history, so nothing was fetched. The release is not implicated; the client's
        /// record of the earlier attempt is what has to go.
        /// </summary>
        Duplicate = 4,

        /// <summary>The client ran out of room before it could finish.</summary>
        DiskSpace = 5,

        /// <summary>
        /// The release arrived and the client could not put it where it belongs. A local
        /// problem: another release would hit the same wall.
        /// </summary>
        PostProcessing = 6,

        /// <summary>The client still lists the download but no longer has its files.</summary>
        MissingFiles = 7
    }
}
