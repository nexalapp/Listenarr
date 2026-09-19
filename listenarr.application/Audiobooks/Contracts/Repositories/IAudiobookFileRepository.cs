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
using Listenarr.Domain.Common;

using Listenarr.Domain.Audiobooks.Chapters;
namespace Listenarr.Application.Audiobooks.Contracts.Repositories
{
    public sealed record AudiobookBasePathMutation(
        int AudiobookId,
        string? ExpectedCurrentBasePath,
        string? ResultingBasePath);

    public sealed record AudiobookFilePathReferenceSnapshot(
        int AudiobookId,
        string? Path);

    public interface IAudiobookFileRepository
    {
        Task<AudiobookFile?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<AudiobookFile>> GetByAudiobookIdAsync(int audiobookId, CancellationToken ct = default);
        Task<List<AudiobookFile>> GetMissingMetadataAsync(int max, CancellationToken ct = default);
        Task<AudiobookFileClaimResult> ClaimAsync(AudiobookFile file, CancellationToken ct = default);
        Task<AudiobookFileClaimResult> ClaimWithBasePathAsync(
            AudiobookFile file,
            AudiobookBasePathMutation basePathMutation,
            CancellationToken ct = default);
        Task<bool> ApplyBasePathAsync(
            AudiobookBasePathMutation basePathMutation,
            CancellationToken ct = default);
        Task<AudiobookFileOwnershipCheckResult> CheckOwnershipAsync(
            int audiobookId,
            int? fileId,
            AudiobookFilePathIdentity identity,
            CancellationToken ct = default);
        Task UpdateAsync(AudiobookFile file, CancellationToken ct = default);

        /// <summary>
        /// Freeze or release the paths of files, and report which files were changed.
        /// </summary>
        /// <param name="fileIds">The files to change.</param>
        /// <param name="locked">True to freeze the paths, false to release them.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The resulting state of every file that exists, keyed by file id.</returns>
        Task<Dictionary<int, bool>> SetPathLockedAsync(
            IReadOnlyCollection<int> fileIds,
            bool locked,
            CancellationToken ct = default);

        /// <summary>
        /// Lock or unlock tags on files, and report every affected file's resulting set.
        /// </summary>
        /// <remarks>
        /// A dedicated write rather than <see cref="UpdateAsync"/> with a mutated entity:
        /// the lock set is the only field being changed, and round-tripping a whole file
        /// through an update would put its path identity — which nothing here is
        /// qualified to restate — back on the wire for the sake of one column.
        /// </remarks>
        /// <param name="fileIds">The files to change.</param>
        /// <param name="tags">The tags to lock or unlock, in catalog casing.</param>
        /// <param name="locked">True to lock the tags, false to release them.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The resulting lock set of every file that exists, keyed by file id.</returns>
        Task<Dictionary<int, List<string>>> SetLockedTagsAsync(
            IReadOnlyCollection<int> fileIds,
            IReadOnlyCollection<string> tags,
            bool locked,
            CancellationToken ct = default);
        Task<bool> ReplacePhysicalGenerationAsync(
            int fileId,
            int audiobookId,
            string? expectedPath,
            string? expectedPhysicalObjectIdentity,
            AudiobookFile replacement,
            CancellationToken ct = default);
        Task<bool> ReplacePhysicalGenerationWithBasePathAsync(
            int fileId,
            int audiobookId,
            string? expectedPath,
            string? expectedPhysicalObjectIdentity,
            AudiobookFile replacement,
            AudiobookBasePathMutation basePathMutation,
            CancellationToken ct = default);
        Task<bool> DeletePhysicalGenerationAsync(
            int fileId,
            int audiobookId,
            string? expectedPath,
            string? expectedPhysicalObjectIdentity,
            CancellationToken ct = default);
        Task<bool> DeletePhysicalGenerationWithBasePathAsync(
            int fileId,
            int audiobookId,
            string? expectedPath,
            string? expectedPhysicalObjectIdentity,
            AudiobookBasePathMutation basePathMutation,
            CancellationToken ct = default);
        Task DeleteByAudiobookIdAsync(int audiobookId, CancellationToken ct = default);
        Task DeleteAsync(int id, CancellationToken ct = default);
        Task<List<string>> GetAllFilePathsAsync(
            FileSystemPathSemantics comparisonSemantics,
            CancellationToken ct = default);
        Task<List<AudiobookFile>> GetAllAsync(CancellationToken ct = default);
        Task<List<AudiobookFilePathReferenceSnapshot>> GetOtherPathReferenceSnapshotsAsync(
            int audiobookId,
            CancellationToken ct = default);
        Task<List<AudiobookFormatSummary>> GetFormatSummariesAsync(CancellationToken ct = default);
        Task<Dictionary<int, int>> GetCountsByAudiobookIdAsync(CancellationToken ct = default);

        /// <summary>
        /// Record what each file's chapters were judged to be, so the books list and the
        /// book page can show it without a probe. A dedicated write for the same reason
        /// as the locks: three columns, not a whole entity round-trip.
        /// </summary>
        Task SetChapterHealthAsync(
            IReadOnlyCollection<AudiobookFileChapterHealth> verdicts,
            CancellationToken ct = default);

        /// <summary>Store the chapter fix worked out for a file, with the key it holds for.</summary>
        Task SetChapterPlanAsync(int fileId, string planJson, string planKey, bool repairable, DateTime plannedAtUtc, CancellationToken ct = default);

        /// <summary>Forget the stored fix for these files, so the next look plans them afresh.</summary>
        Task ClearChapterPlanAsync(IReadOnlyCollection<int> fileIds, CancellationToken ct = default);

        /// <summary>
        /// Flag files a scan could not find. A file already flagged keeps its first
        /// not-found time. Returns the ids newly flagged by this call.
        /// </summary>
        Task<IReadOnlyList<int>> MarkNotFoundAsync(IReadOnlyCollection<int> fileIds, DateTime whenUtc, CancellationToken ct = default);

        /// <summary>A scan found these files again; the flag comes off.</summary>
        Task ClearNotFoundAsync(IReadOnlyCollection<int> fileIds, CancellationToken ct = default);

        /// <summary>The operator's removal of a book's not-found rows. Returns what was removed.</summary>
        Task<IReadOnlyList<AudiobookFileRemoved>> DeleteNotFoundAsync(int audiobookId, CancellationToken ct = default);

        /// <summary>How many of each book's files are flagged not found, for books that have any.</summary>
        Task<Dictionary<int, int>> GetNotFoundCountsByAudiobookIdAsync(CancellationToken ct = default);

        /// <summary>The worst chapter verdict among each book's files, and whether every flagged file is repairable, for books that have one.</summary>
        Task<Dictionary<int, AudiobookChapterSummary>> GetWorstChapterHealthByAudiobookIdAsync(CancellationToken ct = default);
    }

    /// <summary>One file's chapter verdict, as the tag index decided it.</summary>
    public sealed record AudiobookFileChapterHealth(
        int FileId,
        ChapterHealth Health,
        string? Reason,
        int ChapterCount,
        bool Repairable = false);

    /// <summary>A book's worst chapter verdict and whether a repair can do something about it.</summary>
    public sealed record AudiobookChapterSummary(ChapterHealth Health, bool Repairable);

    /// <summary>A file row that was removed, for the history entry and the page that showed it.</summary>
    public sealed record AudiobookFileRemoved(int Id, string? Path);
}
