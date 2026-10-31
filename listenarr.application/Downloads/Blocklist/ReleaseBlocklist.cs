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

namespace Listenarr.Application.Downloads.Blocklist
{
    /// <summary>
    /// The releases blocked for one book, read once and then asked about each search
    /// result.
    ///
    /// A list in hand rather than a query per result: a search returns dozens of results
    /// and a book has at most a handful of blocks, so one read and a walk is both cheaper
    /// and easier to reason about than a round trip inside the scoring loop.
    /// </summary>
    public sealed class ReleaseBlocklist
    {
        private readonly IReadOnlyList<BlockedRelease> _entries;

        public ReleaseBlocklist(IReadOnlyList<BlockedRelease> entries)
        {
            _entries = entries ?? [];
        }

        public static ReleaseBlocklist Empty { get; } = new([]);

        public int Count => _entries.Count;

        /// <summary>Whether this release is one that already failed for this book.</summary>
        public bool IsBlocked(string? releaseTitle, string? releaseId, long sizeBytes) =>
            Find(releaseTitle, releaseId, sizeBytes) != null;

        /// <summary>The block this release matches, for naming it in a log or a message.</summary>
        public BlockedRelease? Find(string? releaseTitle, string? releaseId, long sizeBytes)
        {
            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                if (BlockedReleaseMatch.Matches(entry, releaseTitle, releaseId, sizeBytes))
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
