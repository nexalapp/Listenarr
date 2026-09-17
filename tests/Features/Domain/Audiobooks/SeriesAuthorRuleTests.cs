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

namespace Listenarr.Tests.Features.Domain.Audiobooks
{
    [Trait("Name", "SeriesAuthorRuleTests")]
    [Trait("Category", "Domain")]
    public sealed class SeriesAuthorRuleTests : BaseTests
    {
        /// <summary>
        /// Position, not year: the year a record carries is the audio edition's, and by
        /// that Harry Potter files under the playwright of the 2001-recorded Cursed Child.
        /// </summary>
        [Fact]
        public void Compute_FilesASeriesUnderTheAuthorOfItsLowestPosition()
        {
            var authors = SeriesAuthorRule.Compute(
            [
                new("Harry Potter", "8", 2001, "Jack Thorne"),
                new("Harry Potter", "1", 2015, "J.K. Rowling"),
                new("Dune", null, 2005, "Kevin J. Anderson"),
                new("Dune", "1", 2008, "Frank Herbert"),
            ]);

            Assert.Equal("J.K. Rowling", authors["harry potter"]);
            Assert.Equal("Frank Herbert", authors["dune"]);
        }

        [Fact]
        public void Compute_BreaksAPositionTieByYear_AndAnOverrideWinsOutright()
        {
            var authors = SeriesAuthorRule.Compute(
            [
                new("Jack Daniels", "9", 2013, "Jack Kilborn"),
                new("Jack Daniels", "9", 2011, "Blake Crouch"),
                new("Laddertop", "1", 2011, "Emily Janice Card"),
            ],
            [new("Laddertop", "Orson Scott Card")]);

            Assert.Equal("Blake Crouch", authors["jack daniels"]);
            Assert.Equal("Orson Scott Card", authors["laddertop"]);
        }

        /// <summary>
        /// An anthology has no originator. A blank override leaves the series out of the
        /// snapshot so every book files under its own author.
        /// </summary>
        [Fact]
        public void Compute_ABlankOverrideExemptsTheSeries()
        {
            var authors = SeriesAuthorRule.Compute(
            [
                new("Forward Collection", null, 2019, "Andy Weir"),
                new("Forward Collection", null, 2019, "Blake Crouch"),
                new("Dune", "1", 2008, "Frank Herbert"),
            ],
            [new(" Forward Collection ", "  "), new("", "Nobody")]);

            Assert.False(authors.ContainsKey("forward collection"));
            Assert.Equal("Frank Herbert", authors["dune"]);
        }

        [Fact]
        public void Compute_KeysCaseAndWhitespaceInsensitively_AndSkipsBlanks()
        {
            var authors = SeriesAuthorRule.Compute(
            [
                new(" The Expanse ", "1", 2011, "James S. A. Corey"),
                new("", "1", 2000, "Nobody"),
                new("Orphan", "1", 2000, null),
            ]);

            Assert.Single(authors);
            Assert.Equal("James S. A. Corey", authors[SeriesAuthorRule.Key("the expanse")]);
        }
    }
}
