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

using Listenarr.Application.Audiobooks.Suggestions;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    public sealed class EfSuggestionReleaseRepository(ListenArrDbContext db) : ISuggestionReleaseRepository
    {
        public async Task<IReadOnlyDictionary<string, SuggestionRelease>> GetAllAsync(CancellationToken cancellationToken = default) =>
            (await db.SuggestionReleases.AsNoTracking().ToListAsync(cancellationToken))
                .ToDictionary(release => release.Asin, StringComparer.OrdinalIgnoreCase);

        public Task<SuggestionRelease?> GetAsync(string asin, CancellationToken cancellationToken = default) =>
            db.SuggestionReleases.AsNoTracking().FirstOrDefaultAsync(release => release.Asin == asin, cancellationToken);

        public async Task SaveAsync(SuggestionRelease release, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(release);
            var existing = await db.SuggestionReleases.FirstOrDefaultAsync(r => r.Asin == release.Asin, cancellationToken);
            if (existing == null)
            {
                existing = release;
                db.SuggestionReleases.Add(existing);
            }
            else
            {
                existing.Status = release.Status;
                existing.IndexerName = release.IndexerName;
                existing.ReleaseTitle = release.ReleaseTitle;
                existing.SizeBytes = release.SizeBytes;
                existing.ProtectedResult = release.ProtectedResult;
                existing.Detail = release.Detail;
                existing.CheckedAt = release.CheckedAt;
            }

            await db.SaveChangesAsync(cancellationToken);
            db.Entry(existing).State = EntityState.Detached;
        }
    }
}
