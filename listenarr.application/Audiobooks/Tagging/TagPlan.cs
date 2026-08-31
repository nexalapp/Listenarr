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

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// What will happen to one tag. Reported to the operator before anything is written,
    /// so every outcome that is <em>not</em> a write says why it is not.
    /// </summary>
    public enum TagChangeAction
    {
        /// <summary>The value will be written.</summary>
        Write,

        /// <summary>The file already holds exactly this value.</summary>
        Unchanged,

        /// <summary>The mapping is set to never write this tag.</summary>
        NotConfigured,

        /// <summary>The operator excluded this tag from this run.</summary>
        Deselected,

        /// <summary>The tag is written only when empty, and the file already has a value.</summary>
        Preserved,

        /// <summary>The pattern resolved to nothing for this book.</summary>
        NoValue
    }

    /// <summary>One tag's before and after, with the reason behind it.</summary>
    public sealed record TagChange(
        string Tag,
        string Label,
        string? Current,
        string? Proposed,
        TagChangeAction Action,
        string Reason)
    {
        public bool IsWrite => Action == TagChangeAction.Write;
    }

    /// <summary>
    /// The complete outcome of resolving one book's tags against one file's current tags.
    ///
    /// <para>
    /// <see cref="FinalTags"/> is the whole metadata set the file should end up with, not
    /// just the changed keys. Writing the complete set is what makes a second run produce
    /// the same file rather than a second copy of every tag: the container's existing
    /// metadata is replaced wholesale rather than merged into, so the duplicate
    /// <c>SERIES</c> atoms already in the library collapse to one on the first rewrite.
    /// </para>
    /// </summary>
    public sealed record TagPlan(
        IReadOnlyList<TagChange> Changes,
        IReadOnlyDictionary<string, string> FinalTags)
    {
        public IEnumerable<TagChange> Writes => Changes.Where(change => change.IsWrite);

        public bool HasChanges => Changes.Any(change => change.IsWrite);

        public static TagPlan Empty { get; } = new(
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
