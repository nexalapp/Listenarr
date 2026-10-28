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
namespace Listenarr.Infrastructure.Library.Conversion
{
    public sealed partial class ConversionJobProcessor
    {
        private const int PublishAttempts = 5;

        private static readonly TimeSpan PublishContentionBackoff = TimeSpan.FromSeconds(30);

        /// <summary>SQLite's busy or locked, anywhere in the chain: a wait that ran out, not a fault.</summary>
        internal static bool IsDatabaseContention(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 or 6 })
                {
                    return true;
                }
            }

            return false;
        }
    }
}
