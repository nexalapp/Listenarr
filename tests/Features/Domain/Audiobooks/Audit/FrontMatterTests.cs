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

namespace Listenarr.Tests.Features.Domain.Audiobooks.Audit
{
    /// <summary>
    /// The walk used to stop at the first window that held any words at all, on the grounds
    /// that the story had started. For a book opening on a foreword that is wrong twice: the
    /// foreword is not the story, and a recording that announces itself after one never got
    /// its chance.
    /// </summary>
    [Trait("Name", "FrontMatterTests")]
    [Trait("Category", "Domain")]
    public sealed class FrontMatterTests : BaseTests
    {
        [Theory]
        [Trait("Method", "IsHeading")]
        [Trait("Scenario", "WhatStandsBeforeTheBook")]
        [InlineData("Introduction.")]
        [InlineData("Introduction. When I put together my first short story collection, Infinite Dreams")]
        [InlineData("Foreword")]
        [InlineData("Forward.")]
        [InlineData("Preface")]
        [InlineData("Author's note.")]
        [InlineData("A note on the text")]
        [InlineData("Acknowledgements")]
        [InlineData("“Introduction.”")]
        public void IsHeading_RecognisesFrontMatter(string heard)
        {
            // The first of these is verbatim from Dealing in Futures, whose first chapter is
            // five and a half minutes of Joe Haldeman explaining the collection.
            Assert.True(FrontMatter.IsHeading(heard));
        }

        [Theory]
        [Trait("Method", "IsHeading")]
        [Trait("Scenario", "ProseIsNotAHeading")]
        [InlineData("When I put together my first short story collection, Infinite Dreams")]
        [InlineData("Like a raging mountain, the Terrichik rose screaming from a frozen night-dark sea.")]
        [InlineData("Chapter one.")]
        [InlineData("")]
        public void IsHeading_IsNotFooledByProse(string heard)
        {
            Assert.False(FrontMatter.IsHeading(heard));
        }

        [Theory]
        [Trait("Method", "IsBookStarting")]
        [Trait("Scenario", "TheBookProperBeginning")]
        [InlineData("Chapter one.")]
        [InlineData("Chapter 1")]
        [InlineData("Chapter One. The boy stood at the window.")]
        [InlineData("Part two")]
        [InlineData("Book I")]
        [InlineData("Section 3")]
        public void IsBookStarting_RecognisesADivision(string heard)
        {
            Assert.True(FrontMatter.IsBookStarting(heard));
        }

        [Theory]
        [Trait("Method", "IsBookStarting")]
        [Trait("Scenario", "ABareNumberIsNotAHeading")]
        [InlineData("One.")]
        [InlineData("One of the men turned to look at her.")]
        [InlineData("Introduction.")]
        [InlineData("Seasons. Transcripts edited from the last few hundred hours of recordings.")]
        public void IsBookStarting_NeedsMoreThanANumber(string heard)
        {
            // "One." on its own is as likely to be a sentence as a heading, and the first
            // story of Dealing in Futures is called Seasons - neither says the book has
            // reached a chapter.
            Assert.False(FrontMatter.IsBookStarting(heard));
        }

        [Fact]
        [Trait("Method", "IsHeading")]
        [Trait("Scenario", "OnlyHowTheWindowOpens")]
        public void IsHeading_JudgesOnlyTheFirstLine()
        {
            // A heading further down the window belongs to the passage before it. The walk
            // only ever asks what this window opens on.
            Assert.False(FrontMatter.IsHeading("The wind died.\nIntroduction."));
            Assert.True(FrontMatter.IsHeading("Introduction.\nThe wind died."));
        }
    }
}
