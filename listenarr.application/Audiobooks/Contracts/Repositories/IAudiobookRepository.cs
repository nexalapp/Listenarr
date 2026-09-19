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

using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Common;

namespace Listenarr.Application.Audiobooks.Contracts.Repositories
{
    public sealed record AudiobookPathReferenceSnapshot(
        int AudiobookId,
        string? BasePath,
        string? FilePath);

    public interface IAudiobookRepository
    {
        Task<List<Audiobook>> GetAllAsync();
        Task<AudiobookPathReferenceSnapshot?> GetPathReferenceSnapshotAsync(
            int audiobookId,
            CancellationToken ct = default);
        Task<List<AudiobookPathReferenceSnapshot>> GetOtherPathReferenceSnapshotsAsync(
            int audiobookId,
            CancellationToken ct = default);
        Task<List<Audiobook>> GetLibraryAsync();
        Task<Dictionary<int, List<AudiobookSeriesMembership>>> GetAllSeriesMembershipsGroupedByAudiobookIdAsync(CancellationToken ct = default);

        /// <summary>
        /// How many digits each series needs to write its highest position, keyed by the
        /// series name folded for comparison.
        /// </summary>
        /// <remarks>
        /// The width has to come from the series rather than from the book being named,
        /// because a position only needs widening in the company of its siblings: a
        /// trilogy keeps 1, 2, 3 and only a series that actually reaches ten starts
        /// writing 01. Read from the membership rows and the books' own primary series
        /// together, since a book can carry one without the other.
        /// </remarks>
        Task<Dictionary<string, SeriesPositionStyle>> GetSeriesPositionStylesAsync(CancellationToken ct = default);
        Task<List<Audiobook>> GetByIdsWithFilesAsync(IEnumerable<int> ids, CancellationToken ct = default);
        Task<List<Audiobook>> GetMonitoredAudiobooksForSearchAsync(DateTime cutoff, CancellationToken ct = default);
        Task NormalizeJsonColumnsAsync(CancellationToken ct = default);
        Task<Audiobook?> GetByAsinAsync(string asin);
        Task<Audiobook?> GetByIsbnAsync(string isbn);

        /// <summary>
        /// Every audiobook carrying this identifier, not just the first.
        /// </summary>
        /// <remarks>
        /// An identifier is not unique in this library. Audible publishes several
        /// novellas under one collection ASIN, and two narrations of one book can be
        /// filed against the same product, so deciding whether an incoming book is
        /// already held means comparing against all of its namesakes rather than
        /// whichever one the database happened to return first.
        /// </remarks>
        Task<IReadOnlyList<Audiobook>> GetAllByAsinAsync(string asin);
        Task<IReadOnlyList<Audiobook>> GetAllByIsbnAsync(string isbn);
        Task<Audiobook?> GetByIdAsync(int id);
        Task<Audiobook?> GetByIdSnapshotAsync(int id, CancellationToken ct = default);
        Task<Audiobook?> GetForUpdateSnapshotAsync(int id, CancellationToken ct = default);
        Task<Audiobook?> GetForScanAsync(int id, CancellationToken ct = default);
        Task<Audiobook?> GetForScanSnapshotAsync(int id, CancellationToken ct = default);
        Task<bool> TryUpdateBasePathAsync(
            int audiobookId,
            string expectedBasePath,
            string newBasePath,
            CancellationToken ct = default);
        Task<bool> TryUpdateImageUrlAsync(
            int audiobookId,
            string? expectedImageUrl,
            string? newImageUrl,
            CancellationToken ct = default) => Task.FromResult(false);
        Task<string?> GetAuthorAsinByNameAsync(string name);
        Task<AuthorCacheEntry?> GetCachedAuthorByNameAsync(string name, string region);
        Task<AuthorCacheEntry?> GetCachedAuthorByAsinAsync(string asin, string region);
        Task<AuthorCacheEntry> UpsertCachedAuthorAsync(AuthorCacheEntry authorCacheEntry);
        Task<SeriesCacheEntry?> GetCachedSeriesByNameAsync(string name, string region);
        Task<SeriesCacheEntry?> GetCachedSeriesByAsinAsync(string asin, string region);
        Task<SeriesCacheEntry> UpsertCachedSeriesAsync(SeriesCacheEntry seriesCacheEntry);
        /// <summary>Every cached author catalog, for suggestions; read-only snapshots.</summary>
        Task<List<AuthorCacheEntry>> GetAllCachedAuthorsAsync(CancellationToken ct = default);
        /// <summary>Every cached series catalog, for suggestions; read-only snapshots.</summary>
        Task<List<SeriesCacheEntry>> GetAllCachedSeriesAsync(CancellationToken ct = default);
        Task<List<SuggestionDismissal>> GetSuggestionDismissalsAsync(CancellationToken ct = default);
        /// <summary>Records a dismissal; a repeat of the same key is a no-op.</summary>
        Task AddSuggestionDismissalAsync(SuggestionDismissal dismissal, CancellationToken ct = default);
        Task<bool> RemoveSuggestionDismissalAsync(string key, CancellationToken ct = default);
        Task<Audiobook> AddAsync(Audiobook audiobook);
        Task<bool> UpdateAsync(Audiobook audiobook);

        /// <summary>
        /// Record what the audio audit heard and decided. A dedicated write, as for the
        /// locks: the verdict is the only thing changing, and a whole-entity update
        /// would put every other column back on the wire for the sake of six.
        /// </summary>
        Task SetAudioAuditAsync(int audiobookId, AudioAuditRecord audit, CancellationToken ct = default);
        Task<bool> RewritePathReferencesAsync(
            int audiobookId,
            string? sourceBasePath,
            string targetBasePath,
            FileSystemPathSemantics sourceSemantics,
            FileSystemPathSemantics targetSemantics,
            CancellationToken ct = default,
            FileSystemCaseSensitivityMode targetCaseSensitivityMode = FileSystemCaseSensitivityMode.Auto);
        Task<bool> RewriteMovedPathReferencesAsync(
            int audiobookId,
            string? sourceBasePath,
            string targetBasePath,
            FileSystemPathSemantics sourceSemantics,
            FileSystemPathSemantics targetSemantics,
            IReadOnlyDictionary<string, string> targetPhysicalObjectIdentities,
            DateTime targetPhysicalIdentityObservedAtUtc,
            CancellationToken ct = default,
            FileSystemCaseSensitivityMode targetCaseSensitivityMode = FileSystemCaseSensitivityMode.Auto);
        Task<bool> DeleteByIdAsync(int id);
        Task SaveChangesAsync(CancellationToken ct = default);
        Task<bool> UpdateWithIdentifierReplaceAsync(Audiobook audiobook, List<AudiobookExternalIdentifier> newIdentifiers, CancellationToken ct = default);
    }

    /// <summary>The audit's outcome as it is stored on the book.</summary>
    public sealed record AudioAuditRecord(
        AudioAuditVerdict Verdict,
        string Reason,
        string? Heard,
        AudioCredits Credits,
        DateTime AuditedAtUtc);
}
