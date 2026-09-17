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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Domain.Audiobooks.Chapters;

namespace Listenarr.Application.Audiobooks.Chapters
{
    /// <summary>
    /// One file as the repair preview shows it: its verdict, and either the list a
    /// repair would write or why there is none.
    /// </summary>
    public sealed record ChapterRepairFilePreview(
        int FileId,
        string FileName,
        ChapterHealth Health,
        string? HealthReason,
        ChapterPlan? Plan,
        string? Rejection)
    {
        public bool Repairable => Plan != null;
    }

    public sealed record ChapterRepairPreview(
        int AudiobookId,
        IReadOnlyList<ChapterRepairFilePreview> Files);

    /// <summary>
    /// Decides what a chapter repair would write and queues it.
    ///
    /// <para>
    /// The preview and the enqueue share one planning pass so that what was shown is
    /// what gets written: the plan is serialised onto the job rather than recomputed by
    /// the worker against a file that may read differently by then.
    /// </para>
    /// </summary>
    public interface IChapterRepairService
    {
        /// <param name="audiobookId">The book.</param>
        /// <param name="fileIds">Which of its files, or null for every file with a chapter issue.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<ChapterRepairPreview?> PreviewAsync(
            int audiobookId,
            IReadOnlyCollection<int>? fileIds = null,
            CancellationToken cancellationToken = default);

        /// <summary>Queue a repair for every repairable file in scope.</summary>
        Task<TagEnqueueResult> EnqueueAsync(
            int audiobookId,
            IReadOnlyCollection<int>? fileIds = null,
            CancellationToken cancellationToken = default);
    }
}
