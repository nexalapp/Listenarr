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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks.Conversion
{
    [Trait("Name", "ConvertibleSourceTests")]
    [Trait("Category", "Domain")]
    public sealed class ConvertibleSourceTests : BaseTests
    {
        [Theory]
        // A book in pieces, whatever the pieces are.
        [InlineData("Book - 001.mp3")]
        [InlineData("Book - 001.mp4")]
        [InlineData("Book - 001.m4a")]
        [InlineData("Book - 001.flac")]
        [InlineData("Book - 001.OGG")]
        public void IsConvertible_ReadsAnythingThatIsNotAlreadyTheTarget(string path)
        {
            Assert.True(ConvertibleSource.IsConvertible(path));
        }

        [Theory]
        [InlineData("Book.m4b")]  // already the format conversion produces
        [InlineData("Book.M4B")]
        [InlineData("cover.jpg")]
        [InlineData("release.nfo")]
        [InlineData("")]
        [InlineData(null)]
        public void IsConvertible_LeavesEverythingElseAlone(string? path)
        {
            Assert.False(ConvertibleSource.IsConvertible(path));
        }
    }
}
