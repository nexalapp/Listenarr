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
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence
{
    /// <summary>
    /// Only an I/O error drops the pool. A constraint violation, a busy database or a
    /// bad query says nothing about the connection and must not cost every other
    /// caller their pooled handle.
    /// </summary>
    [Trait("Name", "SqliteIoErrorPoolResetInterceptorTests")]
    [Trait("Category", "Persistence")]
    public sealed class SqliteIoErrorPoolResetInterceptorTests : BaseTests
    {
        [Theory]
        [InlineData(10, true)]   // SQLITE_IOERR
        [InlineData(5, false)]   // SQLITE_BUSY
        [InlineData(19, false)]  // SQLITE_CONSTRAINT
        public void OnlyAnIoErrorCounts(int code, bool expected)
        {
            var exception = new SqliteException("failed", code);

            Assert.Equal(expected, SqliteIoErrorPoolResetInterceptor.IsIoError(exception));
        }

        [Fact]
        public void AnIoErrorWrappedByTheOrmStillCounts()
        {
            var wrapped = new InvalidOperationException("query failed", new SqliteException("disk I/O error", 10));

            Assert.True(SqliteIoErrorPoolResetInterceptor.IsIoError(wrapped));
            Assert.False(SqliteIoErrorPoolResetInterceptor.IsIoError(new InvalidOperationException("no db involved")));
            Assert.False(SqliteIoErrorPoolResetInterceptor.IsIoError(null));
        }

        [Fact]
        public async Task AFailedCommandOnALiveContext_DoesNotBreakTheContext()
        {
            // The interceptor's job is a side effect on the pool; the failure itself must
            // still surface unchanged, and the context must still be usable afterwards.
            var options = new DbContextOptionsBuilder<ProbeContext>()
                .UseSqlite("Data Source=:memory:")
                .AddInterceptors(new SqliteIoErrorPoolResetInterceptor(NullLogger<SqliteIoErrorPoolResetInterceptor>.Instance))
                .Options;
            await using var context = new ProbeContext(options);
            await context.Database.OpenConnectionAsync();

            var thrown = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("SELECT * FROM no_such_table"));

            Assert.NotEqual(SqliteIoErrorPoolResetInterceptor.SqliteIoErr, thrown.SqliteErrorCode);
            await context.Database.ExecuteSqlRawAsync("CREATE TABLE probe (id INTEGER)");
            Assert.Equal(1, await context.Database.ExecuteSqlRawAsync("INSERT INTO probe (id) VALUES (1)"));
        }

        private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options);
    }
}
