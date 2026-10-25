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
namespace Listenarr.Application.Audiobooks.Repair
{
    public enum RepairPreviewState
    {
        Pending,
        Enhancing,
        Ready,
        Failed
    }

    /// <summary>One preview's standing: its clips once Ready, its reason while not.</summary>
    /// <param name="Id">The preview.</param>
    /// <param name="AudiobookId">The book it samples.</param>
    /// <param name="State">Where it has got to.</param>
    /// <param name="Detail">What it is waiting on, or why it failed.</param>
    /// <param name="BeforePath">The untouched window, transcoded through the same encode as the repaired one.</param>
    /// <param name="AfterPath">The enhanced window.</param>
    public sealed record RepairPreview(
        Guid Id,
        int AudiobookId,
        RepairPreviewState State,
        string? Detail,
        string? BeforePath,
        string? AfterPath);

    /// <summary>
    /// A ninety-second A/B before committing a book to days of model time.
    ///
    /// <para>
    /// Not a queue job, deliberately: the repair lane is serial, and a preview parked
    /// behind a two-day repair would answer after the question stopped mattering. It
    /// shares the enhancer gate instead, so it waits at most one chunk of a running
    /// repair and never starts a second model process.
    /// </para>
    /// <para>
    /// Both clips are transcoded through the identical final encode, so the A/B isolates
    /// the enhancement rather than the codec. The measurements' job here is only to have
    /// been honest; the decision belongs to the listener — measured cleanliness has been
    /// overruled by ear three times in this project's history, once for a chain that
    /// measured best and was unintelligible.
    /// </para>
    /// </summary>
    public interface IRepairPreviewService
    {
        /// <summary>
        /// Start a preview of one window. Returns at once; poll <see cref="GetAsync"/>.
        /// A new preview for a book replaces its previous one.
        /// </summary>
        /// <param name="audiobookId">The book.</param>
        /// <param name="fileId">The file to sample, or null for the book's longest.</param>
        /// <param name="startSeconds">Where the window starts, or null for the file's midpoint.</param>
        /// <param name="cancellationToken">Cancels only the start, never a running preview.</param>
        Task<RepairPreview> StartAsync(
            int audiobookId,
            int? fileId = null,
            double? startSeconds = null,
            CancellationToken cancellationToken = default);

        Task<RepairPreview?> GetAsync(Guid previewId, CancellationToken cancellationToken = default);

        /// <summary>
        /// The file behind one of a ready preview's clips, or null while it is not on disk:
        /// a preview past its retention may still be listed after the sweeper took its files.
        /// </summary>
        /// <param name="previewId">The preview.</param>
        /// <param name="variant">"before" or "after".</param>
        string? GetClipPath(Guid previewId, string? variant);
    }
}
