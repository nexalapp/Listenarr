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
    /// <summary>One file's before-and-after, or the reason it could not be read.</summary>
    public sealed record TagPreviewFile(
        int FileId,
        string Name,
        IReadOnlyList<TagChange> Changes,
        string? Error = null)
    {
        public bool HasChanges => Changes.Any(change => change.IsWrite);
    }

    /// <summary>
    /// What a tag write would do to one book, before anything is written.
    ///
    /// Produced by exactly the code that does the writing, against the file's real
    /// current tags — a preview derived from a separate approximation would eventually
    /// disagree with the write, and an operator who has approved a diff is entitled to
    /// get that diff.
    /// </summary>
    public sealed record TagPreview(
        int AudiobookId,
        string? Title,
        bool CanWrite,
        IReadOnlyList<TagPreviewFile> Files,
        string? Reason = null)
    {
        public bool HasChanges => Files.Any(file => file.HasChanges);
    }

    public interface ITagPreviewService
    {
        /// <summary>
        /// Work out what writing tags to this book would change.
        /// </summary>
        /// <remarks>
        /// <c>selectedTags</c> narrows the run the same way it does at write time, so an
        /// operator can tick fields and see the diff shrink before committing to it.
        /// </remarks>
        Task<TagPreview> BuildAsync(
            int audiobookId,
            IReadOnlyCollection<string>? selectedTags = null,
            CancellationToken cancellationToken = default);
    }
}
