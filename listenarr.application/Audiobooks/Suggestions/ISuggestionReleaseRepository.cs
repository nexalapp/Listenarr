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

namespace Listenarr.Application.Audiobooks.Suggestions
{
    /// <summary>The stored outcome of checking suggested books for a downloadable release.</summary>
    public interface ISuggestionReleaseRepository
    {
        /// <summary>Every stored check, keyed by ASIN.</summary>
        Task<IReadOnlyDictionary<string, SuggestionRelease>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<SuggestionRelease?> GetAsync(string asin, CancellationToken cancellationToken = default);

        /// <summary>Records a check, replacing any earlier one for the same ASIN.</summary>
        Task SaveAsync(SuggestionRelease release, CancellationToken cancellationToken = default);
    }
}
