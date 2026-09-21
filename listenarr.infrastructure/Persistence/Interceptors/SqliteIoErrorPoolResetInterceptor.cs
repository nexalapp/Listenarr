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
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Persistence.Interceptors
{
    /// <summary>
    /// Drops the pooled connections when SQLite reports an I/O error.
    ///
    /// A single "disk I/O error" on the NAS turned into every query failing for ninety
    /// seconds, then recovering with no restart. SQLite keeps a connection's state after
    /// an I/O error, and Microsoft.Data.Sqlite pools connections, so one bad moment can
    /// be handed back out for as long as the pooled handle lives. Clearing the pool
    /// makes the next open a fresh handle against the file as it is now. Only idle
    /// pooled connections are affected; ones in use are untouched.
    /// </summary>
    public sealed class SqliteIoErrorPoolResetInterceptor(ILogger<SqliteIoErrorPoolResetInterceptor> logger) : DbCommandInterceptor
    {
        /// <summary>SQLITE_IOERR: the disk, not the SQL, refused the operation.</summary>
        public const int SqliteIoErr = 10;

        // Every I/O error in the burst reports the same thing; one line and one reset
        // per second is plenty.
        private static readonly TimeSpan ResetInterval = TimeSpan.FromSeconds(1);
        private long _lastResetTicks;

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
        {
            ResetPoolIfIoError(eventData.Exception);
            base.CommandFailed(command, eventData);
        }

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            ResetPoolIfIoError(eventData.Exception);
            return base.CommandFailedAsync(command, eventData, cancellationToken);
        }

        public static bool IsIoError(Exception? exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is SqliteException { SqliteErrorCode: SqliteIoErr })
                {
                    return true;
                }
            }

            return false;
        }

        private void ResetPoolIfIoError(Exception? exception)
        {
            if (!IsIoError(exception))
            {
                return;
            }

            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastResetTicks);
            if (now - last < ResetInterval.Ticks || Interlocked.CompareExchange(ref _lastResetTicks, now, last) != last)
            {
                return;
            }

            logger.LogWarning(exception, "SQLite reported an I/O error; dropping pooled connections so the next open starts clean");
            SqliteConnection.ClearAllPools();
        }
    }
}
