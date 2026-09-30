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
    /// A closing that credits the crew. Every line of it is an "X by Y" and none of them
    /// names the book, except the copyright notice, which names only its authors.
    /// </summary>
    [Trait("Name", "AudioCreditsParserCrewTests")]
    [Trait("Category", "Domain")]
    public sealed class AudioCreditsParserCrewTests : BaseTests
    {
        /// <summary>
        /// Verbatim from The Barsoom Project once whisper was made to read it at all. The
        /// parser took "Engineered and directed by Judy Young" - the first "X by Y" in it -
        /// and put the engineer on the record as the author of a Larry Niven novel.
        /// </summary>
        private const string BarsoomClosing = """
            [Music]
            This has been an Audible Frontiers production.
            Executive Producer Steve Feldberg.
            Producers Mike Charzek and Stefan Rudnicki.
            Engineered and directed by Judy Young.
            Edited by Ted Scott.
            Music by Michael Whelan.
            Copyright 1989 by Larry Niven, Stephen Barnes.
            Audio recording copyright 2010 by Audible Inc.
            [Music]
            Audible hopes you have enjoyed this program.
            """;

        [Fact]
        [Trait("Method", "Parse")]
        [Trait("Scenario", "TheCopyrightNoticeNamesTheAuthor")]
        public void Parse_TakesTheAuthorFromTheCopyrightAndNotTheCrew()
        {
            var credits = AudioCreditsParser.Parse(BarsoomClosing);

            Assert.Equal("Larry Niven", credits.Author);
        }

        [Fact]
        [Trait("Method", "Parse")]
        [Trait("Scenario", "AHouseCreditNamesNoBook")]
        public void Parse_FindsNoTitleWhereNoneIsSpoken()
        {
            // "This has been an Audible Frontiers production" credits the house and stops.
            // The book's title is never said aloud in this recording, and inventing one from
            // the words to hand is worse than admitting that.
            var credits = AudioCreditsParser.Parse(BarsoomClosing);

            Assert.Null(credits.Title);
        }

        [Fact]
        [Trait("Method", "Parse")]
        [Trait("Scenario", "NoCrewMemberIsTakenForTheNarrator")]
        public void Parse_FindsNoNarratorAmongTheCrew()
        {
            Assert.Null(AudioCreditsParser.Parse(BarsoomClosing).Narrator);
        }

        [Theory]
        [Trait("Method", "Parse")]
        [Trait("Scenario", "ARealTitleEndingInARoleWordSurvives")]
        [InlineData("Soul Music by Terry Pratchett, read by Nigel Planer.", "Soul Music", "Terry Pratchett")]
        [InlineData("Harper Audio presents Directed Verdict by Randy Singer.", "Directed Verdict", "Randy Singer")]
        public void Parse_StillReadsATitleThatEndsInACrewWord(string transcript, string title, string author)
        {
            // The crew rule matches a clause that is nothing but roles. A title merely
            // ending in one is a book, and there are real ones.
            var credits = AudioCreditsParser.Parse(transcript);

            Assert.Equal(title, credits.Title);
            Assert.Equal(author, credits.Author);
        }

        [Fact]
        [Trait("Method", "Parse")]
        [Trait("Scenario", "AnEditorIsNotTheAuthor")]
        public void Parse_DoesNotTakeAnAnthologysEditorForTheAuthor()
        {
            // Dealing in Futures is a Joe Haldeman collection, and each story carries a note
            // saying where it first appeared. The parser read one of those notes as the book
            // and reported the collection as "Alien Stars, Baen Books, edited" by Elizabeth
            // Mitchell - a confident, wrong answer in place of "nothing was heard".
            const string storyNote = "This story first appeared in Alien Stars, Baen Books, edited by Elizabeth Mitchell.";

            var credits = AudioCreditsParser.Parse(storyNote);

            Assert.Null(credits.Author);
            Assert.Null(credits.Title);
        }

        [Theory]
        [Trait("Method", "Parse")]
        [Trait("Scenario", "OtherRolesThatTakeTheirOwnBy")]
        [InlineData("Translated by Michael Hofmann.")]
        [InlineData("Abridged by Sally Marmion.")]
        [InlineData("Selected by Ursula K. Le Guin.")]
        public void Parse_DoesNotTakeThoseRolesForTheAuthor(string line)
        {
            Assert.Null(AudioCreditsParser.Parse(line).Author);
        }

        [Fact]
        [Trait("Method", "Parse")]
        [Trait("Scenario", "ARealAuthorIsStillRead")]
        public void Parse_StillReadsAPlainAnnouncement()
        {
            // The guard is the word immediately before "by", so an ordinary credit is untouched.
            var credits = AudioCreditsParser.Parse(
                "Audible Frontiers presents The Barsoom Project. Written by Larry Niven and Stephen Barnes and narrated by Stefan Rudnicki.");

            Assert.Equal("Larry Niven and Stephen Barnes", credits.Author);
            Assert.Equal("Stefan Rudnicki", credits.Narrator);
            Assert.Equal("The Barsoom Project", credits.Title);
        }
    }
}
