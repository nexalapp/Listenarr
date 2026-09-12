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
    }
}
