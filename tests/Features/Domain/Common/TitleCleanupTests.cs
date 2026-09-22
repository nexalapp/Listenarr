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

namespace Listenarr.Tests.Features.Domain.Common
{
    [Trait("Name", "TitleCleanupTests")]
    [Trait("Category", "Domain")]
    public sealed class TitleCleanupTests : BaseTests
    {
        [Theory]
        [InlineData("Lock In (Narrated by Wil Wheaton)", "Lock In")]
        [InlineData("Head On (Narrated by Wil Wheaton)", "Head On")]
        [InlineData("Lock In [Read by Amber Benson]", "Lock In")]
        [InlineData("Something (Performed by a Full Cast)", "Something")]
        [InlineData("Lock In  (narrated by WIL WHEATON) ", "Lock In")]
        public void StripNarratorSuffix_TakesTheReaderOutOfTheTitle(string input, string expected)
        {
            Assert.Equal(expected, TitleCleanup.StripNarratorSuffix(input));
        }

        /// <summary>
        /// Only the reader goes. An edition marker says which recording this is and the
        /// narrator field cannot express it; a parenthetical that is part of the book's
        /// own name is nobody's business but the publisher's.
        /// </summary>
        [Theory]
        [InlineData("Dauntless (Dramatized Adaptation)")]
        [InlineData("Harry Potter and the Sorcerer's Stone (Full-Cast Edition)")]
        [InlineData("The Hitchhiker's Guide to the Galaxy (Unabridged)")]
        [InlineData("Sand: Omnibus Edition Part 1")]
        [InlineData("(Un)documented")]
        [InlineData("")]
        [InlineData(null)]
        public void StripNarratorSuffix_LeavesEveryOtherTitleAlone(string? title)
        {
            Assert.Equal(title, TitleCleanup.StripNarratorSuffix(title));
        }

        /// <summary>A title that is only the suffix keeps it; a nameless book is worse.</summary>
        [Fact]
        public void StripNarratorSuffix_KeepsATitleThatIsNothingElse()
        {
            Assert.Equal("(Narrated by Wil Wheaton)", TitleCleanup.StripNarratorSuffix("(Narrated by Wil Wheaton)"));
        }

        [Fact]
        public void StripNarratorSuffix_RemovesMoreThanOne()
        {
            Assert.Equal(
                "Lock In",
                TitleCleanup.StripNarratorSuffix("Lock In (Narrated by Wil Wheaton) (Read by Wil Wheaton)"));
        }
    }
}
