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
using System.Globalization;

namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>
    /// What the two files the audit listened to looked like when it listened, so a
    /// stored transcript can be trusted or thrown away.
    ///
    /// <para>
    /// A timestamp on its own is not enough in either direction. A recording swapped in
    /// with its modification time preserved - <c>cp -p</c>, <c>rsync -a</c>, a restore
    /// from backup - would keep the old book's transcript and be judged as that book.
    /// Size catches those, because two different recordings are practically never the
    /// same number of bytes.
    /// </para>
    /// <para>
    /// Both are recorded rather than a content hash: hashing a 700MB file means reading
    /// all of it, and a tag write rewrites the container without touching a second of
    /// audio, so a hash of the bytes would throw away a good transcript every time a
    /// narrator was corrected. Size and last-write together are cheap, and wrong only
    /// for a replacement that matches on both.
    /// </para>
    /// </summary>
    public static class AudioAuditFileIdentity
    {
        /// <summary>One file as "length:lastWriteTicks"; the parts joined by "|" for several.</summary>
        public static string Of(IEnumerable<(long Length, DateTime LastWriteUtc)> files) =>
            string.Join('|', files.Select(f =>
                f.Length.ToString(CultureInfo.InvariantCulture)
                + ":"
                + f.LastWriteUtc.Ticks.ToString(CultureInfo.InvariantCulture)));

        /// <summary>
        /// Whether what is on disk now is what was listened to. Unknown on either side is
        /// not a match: a transcript with no identity predates this check and is re-taken
        /// once, and a file that cannot be measured is not vouched for.
        /// </summary>
        public static bool Matches(string? stored, string? current) =>
            !string.IsNullOrWhiteSpace(stored)
            && !string.IsNullOrWhiteSpace(current)
            && string.Equals(stored, current, StringComparison.Ordinal);
    }
}
