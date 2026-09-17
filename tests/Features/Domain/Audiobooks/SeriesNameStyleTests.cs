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
    [Trait("Name", "SeriesNameStyleTests")]
    [Trait("Category", "Domain")]
    public sealed class SeriesNameStyleTests : BaseTests
    {
        [Theory]
        [InlineData("Space Odyssey Series", "Space Odyssey")]
        [InlineData("The Southern Reach Trilogy", "The Southern Reach")]
        [InlineData("Commonwealth Saga", "Commonwealth")]
        [InlineData("The Baroque Cycle", "The Baroque")]
        [InlineData("Sprawl Trilogy Series", "Sprawl")]              // stacked, stripped repeatedly
        [InlineData("teixcalaan SERIES", "teixcalaan")]              // case-insensitive
        [InlineData("Alliance-Union Universe Series", "Alliance-Union Universe")]
        public void Render_DropsTheConfiguredTrailingWords(string stored, string expected) =>
            Assert.Equal(expected, SeriesNameStyle.Render(stored, SeriesNameStyle.DefaultDropWords));

        [Theory]
        [InlineData("Wizarding World")]          // the word is the name
        [InlineData("The Sixth World")]
        [InlineData("Forward Collection")]       // not on the default list, on purpose
        [InlineData("Series of Unfortunate Events")] // the word is not at the end
        [InlineData("The Expanse")]
        public void Render_LeavesEveryOtherNameAlone(string stored) =>
            Assert.Equal(stored, SeriesNameStyle.Render(stored, SeriesNameStyle.DefaultDropWords));

        [Fact]
        public void Render_NeverLeavesANameEmpty()
        {
            // A series literally called "Series" keeps its name rather than vanishing
            // into a bracket with nothing in it.
            Assert.Equal("Series", SeriesNameStyle.Render("Series", SeriesNameStyle.DefaultDropWords));
            // The last word standing is kept even when it is itself a drop word.
            Assert.Equal("Trilogy", SeriesNameStyle.Render("Trilogy Series", SeriesNameStyle.DefaultDropWords));
        }

        [Fact]
        public void Render_IsDrivenByTheListItIsGiven()
        {
            Assert.Equal("Space Odyssey Series", SeriesNameStyle.Render("Space Odyssey Series", []));
            Assert.Equal("Wizarding", SeriesNameStyle.Render("Wizarding World", ["world"]));
        }

        [Fact]
        public void ParseDropWords_ReadsTheStoredList_AndFallsBackToTheDefaultWhenUnreadable()
        {
            Assert.Equal(["Series", "Saga"], SeriesNameStyle.ParseDropWords("""[" Series ", "Saga", ""]"""));
            Assert.Empty(SeriesNameStyle.ParseDropWords("[]"));
            Assert.Equal(SeriesNameStyle.DefaultDropWords, SeriesNameStyle.ParseDropWords("not json"));
        }
    }
}
