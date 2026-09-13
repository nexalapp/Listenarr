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
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// What a series needs to know about itself before one of its books can be named:
    /// how wide its positions have to be written so a plain string sort keeps them in
    /// reading order.
    /// </summary>
    public partial class AudiobookRepository
    {
        public async Task<Dictionary<string, int>> GetSeriesPositionWidthsAsync(CancellationToken ct = default)
        {
            var membershipPositions = await _db.AudiobookSeriesMemberships
                .AsNoTracking()
                .Where(membership => membership.SeriesName != null
                    && membership.SeriesNumber != null)
                .Select(membership => new
                {
                    Series = membership.SeriesName!,
                    Position = membership.SeriesNumber!
                })
                .ToListAsync(ct);

            // A book can carry its primary series on the audiobook row without a
            // membership row, so both are read or a series would be measured short.
            var primaryPositions = await _db.Audiobooks
                .AsNoTracking()
                .Where(audiobook => audiobook.Series != null && audiobook.SeriesNumber != null)
                .Select(audiobook => new
                {
                    Series = audiobook.Series!,
                    Position = audiobook.SeriesNumber!
                })
                .ToListAsync(ct);

            var widths = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in membershipPositions.Concat(primaryPositions))
            {
                var key = SeriesNumberFormatting.SeriesKey(entry.Series);
                if (key.Length == 0)
                {
                    continue;
                }

                var width = SeriesNumberFormatting.WidthFor([entry.Position]);
                if (!widths.TryGetValue(key, out var existing) || width > existing)
                {
                    widths[key] = width;
                }
            }

            return widths;
        }
    }
}
