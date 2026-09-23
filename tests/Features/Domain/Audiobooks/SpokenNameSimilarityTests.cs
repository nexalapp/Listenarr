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
    [Trait("Name", "SpokenNameSimilarityTests")]
    [Trait("Category", "Domain")]
    public sealed class SpokenNameSimilarityTests : BaseTests
    {
        /// <summary>
        /// Every one of these was a real flag in a real library: the credited narrator,
        /// and what the transcriber wrote down when it heard that name read aloud.
        /// </summary>
        [Theory]
        [InlineData("Mikael Naramore", "Michael Narrowmore")]
        [InlineData("Mikael Naramore", "Michael Neremore")]
        [InlineData("Emily Woo Zeller", "Emily Wuzallar")]
        [InlineData("P. J. Ochlan", "PJ Oakland")]
        [InlineData("Kyla Garcia", "Kylogarcia")]
        [InlineData("Samara Naeymi", "Samaraniemi")]
        [InlineData("Julian Rhind-Tutt", "Julian Reindtut")]
        [InlineData("Gabrielle de Cuir", "Gabrielle DeCure")]
        [InlineData("Jon Lindstrom", "John Limstrom")]
        [InlineData("Steven Menasche", "Stephen Manash")]
        [InlineData("David de Vries", "David DeVries")]
        [InlineData("Soneela Nankani", "Sunil Anand Kani")]
        [InlineData("Kobna Holdbrook-Smith", "Kabinerhol Brooks-Smith")]
        [InlineData("Lorelei King", "Lara Liking")]
        [InlineData("Adjoa Andoh", "Adjwando")]
        public void IsAnyOf_TheSameNameHeardAndRespelled(string credited, string heard)
        {
            Assert.True(SpokenNameSimilarity.IsAnyOf(heard, [credited]));
        }

        /// <summary>
        /// The direction that must not bend. These were flagged correctly: the audio is a
        /// different reader, and clearing them would rewrite a record from the wrong book.
        /// </summary>
        [Theory]
        [InlineData("Simon Vance", "Victor Garber")]
        [InlineData("Stefan Rudnicki", "Stephen Hoy")]
        [InlineData("Tim Sample", "Penny Sampell")]
        [InlineData("Jason Keller", "Frank Mueller")]
        [InlineData("Peter Kenny", "Toby Longworth")]
        [InlineData("Grover Gardner", "Tom Parker")]
        [InlineData("Dion Graham", "Mark Boyette")]
        [InlineData("David Marantz", "Peter Noble")]
        [InlineData("Jessica Almasy", "Eric Sandwald")]
        [InlineData("Lloyd James", "Roy Avers")]
        public void IsAnyOf_IsNotADifferentReader(string credited, string heard)
        {
            Assert.False(SpokenNameSimilarity.IsAnyOf(heard, [credited]));
        }

        [Fact]
        public void IsAnyOf_MatchesAnyOfSeveralCreditedNarrators()
        {
            Assert.True(SpokenNameSimilarity.IsAnyOf(
                "Michael Narrowmore",
                ["Stefan Rudnicki", "Mikael Naramore", "Gabrielle de Cuir"]));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void IsAnyOf_IsFalseWhenNothingWasHeard(string? heard)
        {
            Assert.False(SpokenNameSimilarity.IsAnyOf(heard, ["Mikael Naramore"]));
        }

        [Fact]
        public void IsAnyOf_IsFalseWhenNoNarratorIsCredited()
        {
            Assert.False(SpokenNameSimilarity.IsAnyOf("Michael Narrowmore", []));
            Assert.False(SpokenNameSimilarity.IsAnyOf("Michael Narrowmore", null));
        }

        [Fact]
        public void Best_RisesWithAgreement()
        {
            var same = SpokenNameSimilarity.Best("Michael Narrowmore", ["Mikael Naramore"]);
            var other = SpokenNameSimilarity.Best("Frank Mueller", ["Mikael Naramore"]);

            Assert.True(same > other, $"expected {same} > {other}");
            Assert.True(same >= SpokenNameSimilarity.SameNameThreshold);
        }
    }
}
