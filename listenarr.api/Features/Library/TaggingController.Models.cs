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

namespace Listenarr.Api.Features.Library
{
    /// <summary>
    /// What one tagging run should write.
    /// </summary>
    /// <remarks>
    /// <c>Tags</c> names the tags to write, or is null for every tag the mapping allows.
    /// <c>Values</c> carries what the operator typed in the preview, replacing what
    /// those tags' patterns would produce — a provider's wrong series position is
    /// correctable for one book without editing the mapping every book shares.
    /// </remarks>
    public sealed class WriteTagsRequest
    {
        public List<string>? Tags { get; set; }

        public Dictionary<string, string>? Values { get; set; }
    }

    /// <summary>
    /// Which tags may not be written on which files.
    /// </summary>
    /// <remarks>
    /// One request covers many files and many tags because the table it is driven from
    /// does: locking a column across a selection is one click there, and one round trip
    /// here rather than a hundred.
    /// </remarks>
    public sealed class SetTagLocksRequest
    {
        public List<int> FileIds { get; set; } = [];

        public List<string> Tags { get; set; } = [];

        /// <summary>True to lock the tags, false to release them.</summary>
        public bool Locked { get; set; }
    }

    /// <summary>
    /// Which files' paths may not be changed by organizing.
    /// </summary>
    public sealed class SetPathLocksRequest
    {
        public List<int> FileIds { get; set; } = [];

        /// <summary>True to freeze the paths, false to release them.</summary>
        public bool Locked { get; set; }
    }
}
