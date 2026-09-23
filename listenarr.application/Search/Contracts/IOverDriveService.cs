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
namespace Listenarr.Application.Search.Contracts
{
    /// <summary>One audiobook edition as a library lending catalogue describes it.</summary>
    /// <param name="Id">OverDrive's own identifier for the edition.</param>
    /// <param name="Title">The edition's title.</param>
    /// <param name="Authors">Everyone credited as author.</param>
    /// <param name="Narrators">Everyone credited as narrator, which is why this source is here.</param>
    /// <param name="Publisher">The imprint, which is often what tells two recordings apart.</param>
    /// <param name="RuntimeMinutes">How long the recording runs, for checking against the file.</param>
    /// <param name="PublishYear">Year of this edition, not of the novel.</param>
    /// <param name="ImageUrl">Cover art, where the catalogue carries it.</param>
    public sealed record OverDriveEdition(
        string Id,
        string Title,
        IReadOnlyList<string> Authors,
        IReadOnlyList<string> Narrators,
        string? Publisher,
        int? RuntimeMinutes,
        string? PublishYear,
        string? ImageUrl);

    /// <summary>
    /// Editions as public library catalogues know them.
    ///
    /// <para>
    /// Audible sells what Audible sells. A library lends what publishers license to
    /// libraries, which is a different and partly disjoint set: the Tantor recording of
    /// The Scarlet Pimpernel read by Wanda McCaddon is in one and not the other. Where a
    /// book's audio names a reader no shop admits exists, this is worth asking.
    /// </para>
    /// <para>
    /// It is not a complete answer. A library catalogue is thin on pre-2000 cassette
    /// releases and on UK-only imprints, which is where a heard name still has to be
    /// written by hand.
    /// </para>
    /// </summary>
    public interface IOverDriveService
    {
        /// <summary>
        /// Editions matching a title and author, narrators included. An empty list means
        /// the catalogue has nothing; it does not distinguish that from a bad moment,
        /// because a caller offering extra candidates does not need the difference.
        /// </summary>
        Task<IReadOnlyList<OverDriveEdition>> SearchAsync(
            string? title,
            string? author,
            CancellationToken cancellationToken = default);
    }
}
