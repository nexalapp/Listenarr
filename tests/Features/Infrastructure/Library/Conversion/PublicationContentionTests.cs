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
using Listenarr.Infrastructure.Library.Conversion;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Conversion
{
    /// <summary>
    /// A publication that ran into a busy database is tried again rather than failed: for
    /// a repair the original is already archived, and failing left a book with no audio.
    /// </summary>
    [Trait("Name", "PublicationContentionTests")]
    [Trait("Category", "Repair")]
    public sealed class PublicationContentionTests : BaseTests
    {
        [Fact]
        public void ABusyOrLockedDatabase_IsContention_HoweverDeepItIsWrapped()
        {
            Assert.True(ConversionJobProcessor.IsDatabaseContention(new SqliteException("database is locked", 5)));
            Assert.True(ConversionJobProcessor.IsDatabaseContention(
                new DbUpdateException("save failed", new SqliteException("database table is locked", 6))));
        }

        [Fact]
        public void OtherFailures_AreNot()
        {
            Assert.False(ConversionJobProcessor.IsDatabaseContention(new SqliteException("constraint failed", 19)));
            Assert.False(ConversionJobProcessor.IsDatabaseContention(new IOException("pinned parent changed")));
        }
    }
}
