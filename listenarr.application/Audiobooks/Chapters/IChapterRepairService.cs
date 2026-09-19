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
    /// <remarks>
    /// <c>PlanPending</c> means the file is flagged and its fix has not been worked out
    /// yet; a planning job has been queued for it.
    /// </remarks>
    public sealed record ChapterRepairFilePreview(
        int FileId,
        string FileName,
        ChapterHealth Health,
        string? HealthReason,
        ChapterPlan? Plan,
        string? Rejection,
        bool PlanPending = false)
    {
        public bool Repairable => Plan != null;
    }

    /// <summary>
    /// A planning pass could not finish because a source it needed — Audnexus, the
    /// whisper model, a mark's audio — was not there. Nothing was stored for the files
    /// concerned; the job that ran it fails and is retried.
    /// </summary>
    public sealed class ChapterSourceUnavailableException(string message) : Exception(message);

    public sealed record ChapterRepairPreview(
        int AudiobookId,
        IReadOnlyList<ChapterRepairFilePreview> Files);

    /// <summary>
    /// One file's chapters as they are: the list it plays, the verdict, and what the
    /// atoms say — for a page that shows chapters rather than deciding anything.
    /// </summary>
    public sealed record ChapterDescription(
        int FileId,
        string FileName,
        ChapterHealth Health,
        string? Reason,
        IReadOnlyList<Domain.Audiobooks.Conversion.EmbeddedChapter> Chapters,
        ChapterAtomState? Atoms,
        TimeSpan Duration,
        string? Error,
        bool Repairable = false,
        ChapterPlanOutcome? Proposal = null,
        bool PlanPending = false);

    /// <summary>
    /// Decides what a chapter repair would write and queues it.
    ///
    /// <para>
    /// Only <see cref="PlanAsync"/> works a plan out, and only the queue's worker calls
    /// it. The preview and the enqueue read the plan stored on the file, so what was
    /// shown is what gets written and no request waits on Audnexus or whisper.
    /// </para>
    /// </summary>
    public interface IChapterRepairService
    {
        /// <summary>The stored fix for each file in scope. Files without one are marked pending and queued for planning.</summary>
        /// <param name="audiobookId">The book.</param>
        /// <param name="fileIds">Which of its files, or null for every file with a chapter issue.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<ChapterRepairPreview?> PreviewAsync(
            int audiobookId,
            IReadOnlyCollection<int>? fileIds = null,
            CancellationToken cancellationToken = default);

        /// <summary>Read every M4B of the book and describe its chapters. Nothing is planned or written.</summary>
        Task<IReadOnlyList<ChapterDescription>?> DescribeAsync(int audiobookId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Work out and store the fix for the files in scope (all flagged files when null).
        /// Throws <see cref="ChapterSourceUnavailableException"/> when a source could not be
        /// asked, with nothing stored for those files.
        /// </summary>
        Task<ChapterRepairPreview?> PlanAsync(
            int audiobookId,
            IReadOnlyCollection<int>? fileIds = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>Forget the stored fix for the files in scope and queue planning again.</summary>
        Task<TagEnqueueResult> ReplanAsync(int audiobookId, IReadOnlyCollection<int>? fileIds = null, CancellationToken cancellationToken = default);

        /// <summary>Queue a repair for every repairable file in scope.</summary>
        Task<TagEnqueueResult> EnqueueAsync(
            int audiobookId,
            IReadOnlyCollection<int>? fileIds = null,
            CancellationToken cancellationToken = default);
    }
}
