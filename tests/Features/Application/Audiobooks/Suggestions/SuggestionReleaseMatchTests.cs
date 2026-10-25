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

using Listenarr.Application.Audiobooks.Suggestions;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Suggestions
{
    /// <summary>
    /// Which releases count as a suggested book. The cases are the ones the first sweep
    /// of the series suggestions got wrong.
    /// </summary>
    [Trait("Name", "SuggestionReleaseMatchTests")]
    [Trait("Category", "Application")]
    public sealed class SuggestionReleaseMatchTests : BaseTests
    {
        [Theory]
        // A role after the author is no part of their name.
        [InlineData("Grantville Gazette, Volume I", "Eric Flint", "Grantville Gazette, Volume I", "Eric Flint - editor", true)]
        [InlineData("Ring of Fire I", "Eric Flint (Editor)", "Ring of Fire I", "Eric Flint (Editor)", true)]
        // A post without the subtitle is still the book, when the main title is distinctive.
        [InlineData("Solaris: The Definitive Edition", "Stanislaw Lem", "Solaris", "Stanislaw Lem", true)]
        // A main title shared by a whole series is not enough; the subtitle must agree.
        [InlineData("1636: The Saxon Uprising", "Eric Flint", "1636: The Barbie Consortium", "Eric Flint", false)]
        [InlineData("1636: The Barbie Consortium", "Gorg Huff, Paula Goodlett", "1636: The Barbie Consortium", "Gorg Huff", true)]
        [InlineData("1636", "Gorg Huff", "1636: The Barbie Consortium", "Gorg Huff", false)]
        [InlineData("Breakaway", "Craig Alanson", "Breakaway", "Someone Else", false)]
        public void AnAbookLinkPostWithoutAnAsin(string postTitle, string postAuthor, string bookTitle, string bookAuthor, bool expected)
        {
            Assert.Equal(expected, SuggestionReleaseMatch.AbookPostMatches(
                null, postTitle, postAuthor, "B000000000", bookTitle, [bookAuthor]));
        }

        [Theory]
        // The post's title is only its series name, so its position decides: this post is
        // book 4 and matched Gauntlet Wars book 1 on title and author.
        [InlineData("Gauntlet Wars", "Gauntlet Wars", "04", "Gauntlet Wars", "1", false)]
        [InlineData("Gauntlet Wars", "Gauntlet Wars", "04", "Gauntlet Wars", "4", true)]
        [InlineData("Gauntlet Wars", "Gauntlet Wars", null, "Gauntlet Wars", "1", false)]
        // A title that names the book is not second-guessed by a position that disagrees:
        // a parse of "1 633 - 1633" as 633, or a series numbered two ways.
        [InlineData("1633", "Ring of Fire", "633", "1633", "3", true)]
        [InlineData("Partner In Crime", "Joanna Brady", "10", "Partner in Crime", "16", true)]
        [InlineData("Den Of Iniquity", "Stone Barrington", "26", "Den of Iniquity", "23", true)]
        public void APostTitledOnlyWithItsSeriesMustHaveTheBooksPosition(
            string postTitle, string postSeries, string? postPosition, string bookTitle, string bookNumber, bool expected)
        {
            Assert.Equal(expected, SuggestionReleaseMatch.AbookPostMatches(
                null, postTitle, "Some Author", "B000000000", bookTitle, ["Some Author"],
                postSeries, postPosition, bookNumber));
        }

        [Fact]
        public void APostThatDeclaresAnAsinIsHeldToIt()
        {
            Assert.False(SuggestionReleaseMatch.AbookPostMatches(
                "B000OTHER0", "Solaris", "Stanislaw Lem", "B0053ZT602", "Solaris", ["Stanislaw Lem"]));
        }

        [Theory]
        [InlineData("1636: The Barbie Consortium", "Gorg Huff", "1636: The Barbie Consortium Gorg Huff")]
        [InlineData("Sten: Court of a Thousand Suns", "Allan Cole", "Sten: Court of a Thousand Suns Allan Cole")]
        [InlineData("Solaris: The Definitive Edition", "Stanislaw Lem", "Solaris Stanislaw Lem")]
        [InlineData("Grantville Gazette, Volume I", "Eric Flint - editor", "Grantville Gazette, Volume I Eric Flint")]
        public void TheSearchCarriesWhatTellsTheBookApart(string title, string author, string expected)
        {
            Assert.Equal(expected, SuggestionReleaseMatch.QueryFor(title, [author]));
        }
    }
}
