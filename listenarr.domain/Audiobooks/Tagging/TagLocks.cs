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
namespace Listenarr.Domain.Audiobooks.Tagging
{
    /// <summary>
    /// Reading a file's tag locks the way the planner needs them.
    /// </summary>
    /// <remarks>
    /// Shared rather than restated at each of the three places that plan a write —
    /// the preview, the library table and the job — because the three have to agree.
    /// A table that showed a tag as locked while the job that ran a minute later wrote
    /// it anyway would be worse than no lock at all.
    /// </remarks>
    public static class TagLocks
    {
        /// <summary>
        /// The tags locked on this file, or null when none are.
        /// </summary>
        /// <remarks>
        /// Null rather than an empty set, because the planner reads null as "no lock set
        /// applies" and an empty set would be the same answer at more cost.
        /// </remarks>
        public static IReadOnlySet<string>? Of(AudiobookFile? file) =>
            file?.LockedTags is { Count: > 0 } locked
                ? new HashSet<string>(locked, StringComparer.OrdinalIgnoreCase)
                : null;
    }
}
