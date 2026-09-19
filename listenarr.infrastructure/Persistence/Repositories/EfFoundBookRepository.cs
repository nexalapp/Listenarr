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
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Domain.FoundBooks;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    public sealed class EfFoundBookRepository(ListenArrDbContext db) : IFoundBookRepository
    {
        public async Task<IReadOnlyList<FoundBook>> GetAllAsync(CancellationToken cancellationToken = default) =>
            await db.FoundBooks.AsNoTracking().OrderBy(b => b.Id).ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<FoundBook>> GetByWatchFolderAsync(string watchFolder, CancellationToken cancellationToken = default) =>
            await db.FoundBooks.AsNoTracking()
                .Where(b => b.WatchFolder == watchFolder)
                .OrderBy(b => b.Id)
                .ToListAsync(cancellationToken);

        public Task<FoundBook?> GetAsync(int id, CancellationToken cancellationToken = default) =>
            db.FoundBooks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        public async Task<FoundBook> AddAsync(FoundBook book, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(book);
            db.FoundBooks.Add(book);
            await db.SaveChangesAsync(cancellationToken);
            db.Entry(book).State = EntityState.Detached;
            return book;
        }

        public async Task<bool> UpdateAsync(int id, Action<FoundBook> mutate, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(mutate);
            var book = await db.FoundBooks.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (book == null)
            {
                return false;
            }

            mutate(book);
            await db.SaveChangesAsync(cancellationToken);
            db.Entry(book).State = EntityState.Detached;
            return true;
        }

        public async Task<int> DeleteAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0)
            {
                return 0;
            }

            // Load-then-remove rather than ExecuteDelete so the in-memory provider the
            // tests run on behaves the same as SQLite.
            var rows = await db.FoundBooks.Where(b => list.Contains(b.Id)).ToListAsync(cancellationToken);
            db.FoundBooks.RemoveRange(rows);
            await db.SaveChangesAsync(cancellationToken);
            return rows.Count;
        }
    }
}
