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
    [Trait("Name", "TitleCasingTests")]
    [Trait("Category", "Domain")]
    public sealed class TitleCasingTests : BaseTests
    {
        [Theory]
        [InlineData("Fortress of owls", "Fortress of Owls")]
        [InlineData("Fortress in the eye of time", "Fortress in the Eye of Time")]
        [InlineData("The cat who walks through walls", "The Cat Who Walks Through Walls")]
        [InlineData("Exile's gate", "Exile's Gate")]
        [InlineData("Scatter, adapt, and remember", "Scatter, Adapt, and Remember")]
        [InlineData("2010, odyssey two", "2010, Odyssey Two")]
        [InlineData("Nebula awards 33", "Nebula Awards 33")]
        [InlineData("The collected stories of Philip K. Dick", "The Collected Stories of Philip K. Dick")]
        [InlineData("Evil is a Matter of Perspective", "Evil Is a Matter of Perspective")]
        public void ToTitleCase_RaisesASentenceCasedTitle(string input, string expected)
        {
            Assert.Equal(expected, TitleCasing.ToTitleCase(input));
        }

        /// <summary>
        /// Upgrade-only: a title that already carries its publisher's casing is the
        /// publisher's business, including a small word they chose to capitalise and a
        /// particle they chose not to.
        /// </summary>
        [Theory]
        [InlineData("The Adventures of Amina al-Sirafi")]
        [InlineData("Shadows upon Time")]
        [InlineData("Ready Player One")]
        [InlineData("1984")]
        [InlineData("")]
        public void ToTitleCase_LeavesAnAlreadyCasedTitleAlone(string title)
        {
            Assert.Equal(title, TitleCasing.ToTitleCase(title));
        }

        [Theory]
        [InlineData("a memory called empire", "A Memory Called Empire")]
        [InlineData("the way of kings: a novel", "The Way of Kings: A Novel")]
        [InlineData("what we talk about", "What We Talk About")]
        public void ToTitleCase_CapitalisesASmallWordAtABoundary(string input, string expected)
        {
            Assert.Equal(expected, TitleCasing.ToTitleCase(input));
        }
    }
}
