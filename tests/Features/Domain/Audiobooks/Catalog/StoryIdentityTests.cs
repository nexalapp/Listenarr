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
using Listenarr.Domain.Audiobooks.Catalog;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks.Catalog
{
    [Trait("Name", "StoryIdentityTests")]
    [Trait("Category", "Domain")]
    public sealed class StoryIdentityTests : BaseTests
    {
        [Theory]
        [InlineData("Wool", "wool")]
        [InlineData("Wool: The Silo Saga", "wool")]
        [InlineData("Wool (Unabridged)", "wool")]
        [InlineData("Project Hail Mary: A Novel", "project hail mary")]
        [InlineData("Dust — Special Edition", "dust")]
        [InlineData("The Martian: Movie Tie-In Edition", "the martian")]
        public void TitleKey_ReducesAPublicationToTheStoryItNames(string title, string expected)
        {
            Assert.Equal(expected, StoryIdentity.TitleKey(title));
        }

        [Fact]
        public void TitleKey_KeepsATitleThatIsNothingButPackaging()
        {
            Assert.Equal("a novel", StoryIdentity.TitleKey("A Novel"));
            Assert.Equal(string.Empty, StoryIdentity.TitleKey(null));
        }

        [Theory]
        [InlineData("Silo", "1", "position:silo:1")]
        [InlineData("Silo", " 2.5 ", "position:silo:2.5")]
        [InlineData("The Expanse", "03", "position:the expanse:3")]
        public void PositionKey_NamesAPlaceInASeries(string series, string number, string expected)
        {
            Assert.Equal(expected, StoryIdentity.PositionKey(series, number));
        }

        [Theory]
        [InlineData("Silo", "1-3")]       // an omnibus stands in for no single story
        [InlineData("Silo", "Book One")]
        [InlineData("Silo", null)]
        [InlineData(null, "1")]
        public void PositionKey_RefusesAnythingThatIsNotAPlace(string? series, string? number)
        {
            Assert.Null(StoryIdentity.PositionKey(series, number));
        }

        [Fact]
        public void TitleAuthorKey_KeepsTwoAuthorsBooksOfOneNameApart()
        {
            var howey = StoryIdentity.TitleAuthorKey("Wool", ["Hugh Howey"]);
            var other = StoryIdentity.TitleAuthorKey("Wool", ["Someone Else"]);

            Assert.NotNull(howey);
            Assert.NotEqual(howey, other);
            // Order of co-authors does not change the key.
            Assert.Equal(
                StoryIdentity.TitleAuthorKey("Wool", ["A", "B"]),
                StoryIdentity.TitleAuthorKey("Wool", ["B", "A"]));
        }

        [Fact]
        public void TitleAuthorKey_IsNothingWithoutBothHalves()
        {
            Assert.Null(StoryIdentity.TitleAuthorKey("Wool", []));
            Assert.Null(StoryIdentity.TitleAuthorKey(null, ["Hugh Howey"]));
        }

        [Fact]
        public void KeysFor_OffersThePlaceAndTheTitle()
        {
            var keys = StoryIdentity.KeysFor("Wool: The Silo Saga", ["Hugh Howey"], "Silo", "1").ToList();

            Assert.Equal(["position:silo:1", "story:wool::hugh howey"], keys);
        }
    }
}
