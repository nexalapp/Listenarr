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
    /// <summary>One persisted probe result, as <see cref="LibraryTagCache"/> holds it.</summary>
    public sealed record LibraryTagCacheRecord(
        string Path,
        long Length,
        DateTime LastWriteUtc,
        AudiobookFileTags Tags);

    /// <summary>
    /// Durable home for the tag cache, so a restart costs nothing and the tag table opens
    /// at once. The in-memory <see cref="LibraryTagCache"/> is still what a load reads;
    /// this is where it is filled from and written back to.
    /// </summary>
    public interface ILibraryTagCacheStore
    {
        Task<IReadOnlyList<LibraryTagCacheRecord>> LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>Insert or replace each record by path.</summary>
        Task SaveAsync(IReadOnlyList<LibraryTagCacheRecord> records, CancellationToken cancellationToken = default);

        Task ClearAsync(CancellationToken cancellationToken = default);
    }
}
