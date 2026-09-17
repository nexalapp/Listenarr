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
using Listenarr.Infrastructure.Persistence.Interceptors;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence
{
    /// <summary>
    /// The alias setting is applied at save time, whichever code path saves, so a
    /// provider spelling never reaches the database.
    /// </summary>
    [Trait("Name", "AuthorAliasSaveInterceptorTests")]
    [Trait("Category", "Persistence")]
    public sealed class AuthorAliasSaveInterceptorTests : BaseTests
    {
        [Fact]
        public async Task SaveChanges_RewritesAliasedAuthorsOnAddAndUpdate()
        {
            var snapshot = new ApplicationSettingsSnapshot();
            snapshot.Update(new ApplicationSettings
            {
                AuthorAliasesJson = """[{"variant":"B.V. Larson","canonical":"B. V. Larson"},{"variant":"Daniel May","canonical":"Daniel Thomas May"}]"""
            });

            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new AuthorAliasSaveInterceptor(snapshot))
                .Options;

            await using var db = new ListenArrDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var book = new Audiobook { Title = "Starship Pandora", Authors = ["B.V. Larson"], Narrators = ["Daniel May"] };
            db.Audiobooks.Add(book);
            await db.SaveChangesAsync();
            Assert.Equal(["B. V. Larson"], book.Authors);
            Assert.Equal(["Daniel Thomas May"], book.Narrators);

            book.Authors = ["b.v. larson", "Gentry Lee"];
            await db.SaveChangesAsync();
            Assert.Equal(["B. V. Larson", "Gentry Lee"], book.Authors);

            var stored = await db.Audiobooks.AsNoTracking().SingleAsync();
            Assert.Equal(["B. V. Larson", "Gentry Lee"], stored.Authors);
        }

        [Fact]
        public async Task SaveChanges_LeavesAuthorsAloneWithoutAliases()
        {
            var snapshot = new ApplicationSettingsSnapshot();
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new AuthorAliasSaveInterceptor(snapshot))
                .Options;

            await using var db = new ListenArrDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var book = new Audiobook { Title = "Starship Pandora", Authors = ["B.V. Larson"] };
            db.Audiobooks.Add(book);
            await db.SaveChangesAsync();

            Assert.Equal(["B.V. Larson"], book.Authors);
        }
    }
}
