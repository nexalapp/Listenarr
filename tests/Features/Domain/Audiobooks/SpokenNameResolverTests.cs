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
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks
{
    [Trait("Name", "SpokenNameResolverTests")]
    [Trait("Category", "Domain")]
    public sealed class SpokenNameResolverTests : BaseTests
    {
        /// <summary>The narrators one real library already knew when these were heard.</summary>
        private static readonly string[] Known =
        [
            "Garrick Hagon", "George Guidall", "Wanda McCaddon", "Angela Dawe", "Phil Gigante",
            "Scott Brick", "Simon Vance", "Jonathan Davis", "Bill Homewood", "Mikael Naramore"
        ];

        [Theory]
        [InlineData("Garak Hagen", "Garrick Hagon")]
        [InlineData("George Guadal", "George Guidall")]
        [InlineData("Wanda McCadden", "Wanda McCaddon")]
        [InlineData("Phil Giganti", "Phil Gigante")]
        [InlineData("Angela Daw", "Angela Dawe")]
        [InlineData("Michael Narrowmore", "Mikael Naramore")]
        public void Resolve_SpellsAHeardNameTheWayTheLibraryDoes(string heard, string expected)
        {
            var resolved = SpokenNameResolver.Resolve(heard, Known);

            Assert.Equal(expected, resolved.Resolved);
            Assert.True(resolved.WasRecognised);
            Assert.Equal(heard, resolved.Heard);
        }

        [Fact]
        public void Resolve_KeepsAnExactSpellingAndIsCertainOfIt()
        {
            var resolved = SpokenNameResolver.Resolve("Scott Brick", Known);

            Assert.Equal("Scott Brick", resolved.Resolved);
            Assert.Equal(1, resolved.Confidence);
        }

        /// <summary>
        /// A reader the library has never heard of is written as heard. Snapping them to
        /// the nearest known name is how a record gets the wrong narrator.
        /// </summary>
        [Theory]
        [InlineData("Victor Garber")]
        [InlineData("Vanessa Moroni")]
        [InlineData("Toby Longworth")]
        public void Resolve_PassesThroughANameItDoesNotKnow(string heard)
        {
            var resolved = SpokenNameResolver.Resolve(heard, Known);

            Assert.Equal(heard, resolved.Resolved);
            Assert.False(resolved.WasRecognised);
        }

        /// <summary>
        /// Two known names equally near the heard one is a coin toss, not a correction.
        /// </summary>
        [Fact]
        public void Resolve_RefusesToChooseBetweenTwoEquallyNearNames()
        {
            var resolved = SpokenNameResolver.Resolve("Jon Smyth", ["Jon Smith", "John Smythe"]);

            Assert.Equal("Jon Smyth", resolved.Resolved);
            Assert.False(resolved.WasRecognised);
        }

        [Theory]
        [InlineData("Angela Dawe and Phil Gigante", 2)]
        [InlineData("Angela Daw & Phil Giganti", 2)]
        [InlineData("Stefan Rudnicki, Gabrielle de Cuir and Mur Lafferty", 3)]
        [InlineData("Scott Brick", 1)]
        public void Split_SeparatesTheReadersOfOneCredit(string heard, int expected)
        {
            Assert.Equal(expected, SpokenNameResolver.Split(heard).Count);
        }

        [Fact]
        public void ResolveEach_SpellsEveryReaderOfATwoHander()
        {
            var resolved = SpokenNameResolver.ResolveEach("Angela Daw and Phil Giganti", Known);

            Assert.Equal(["Angela Dawe", "Phil Gigante"], resolved.Select(r => r.Resolved));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_SaysNothingAboutNothing(string? heard)
        {
            var resolved = SpokenNameResolver.Resolve(heard, Known);

            Assert.False(resolved.WasRecognised);
            Assert.Empty(SpokenNameResolver.Split(heard));
        }
    }
}
