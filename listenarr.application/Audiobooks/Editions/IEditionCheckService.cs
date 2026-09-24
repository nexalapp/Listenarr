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

namespace Listenarr.Application.Audiobooks.Editions
{
    /// <summary>
    /// Works out which edition of a book the files on disk actually are, by asking the
    /// catalogue how long each edition runs and comparing.
    ///
    /// <para>
    /// Deliberately on demand and never stored. The answer depends on a catalogue that
    /// changes under it, and a badge written into the library on the strength of one
    /// lookup is how a wrong verdict becomes permanent. The caller asks about one book,
    /// reads the sentence and decides.
    /// </para>
    /// </summary>
    public interface IEditionCheckService
    {
        Task<EditionMatchResult> CheckAsync(int audiobookId, CancellationToken cancellationToken = default);
    }
}
