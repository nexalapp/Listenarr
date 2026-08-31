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
    /// Whether one tag may be overwritten.
    ///
    /// This is per tag rather than per run because the two kinds of tag differ: a
    /// description Listenarr fetched from Audible should replace whatever a release
    /// shipped with, while a hand-corrected album name should not be reverted the next
    /// time the book is enriched.
    /// </summary>
    public enum TagWriteMode
    {
        /// <summary>Leave the tag exactly as the file has it. Never written, never cleared.</summary>
        Never,

        /// <summary>Write only when the file carries no value for this tag.</summary>
        WhenEmpty,

        /// <summary>Write whenever the resolved value differs from what the file carries.</summary>
        Always
    }
}
