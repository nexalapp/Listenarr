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
    /// The candidate rule for titles the catalogue has branded.
    ///
    /// <para>
    /// Audible names a continuation novel after the franchise's dead author while the
    /// forum names it after the book and credits the brand as an author. Neither is
    /// wrong and the two titles can never be equal, so thirty-four books in this library
    /// read as unavailable while sitting on the forum.
    /// </para>
    /// <para>
    /// The cases that must keep failing are the ones that have already cost something: a
    /// one-word title answering for a sequel, and a numbered book answering for its
    /// unnumbered sibling.
    /// </para>
    /// </summary>
    [Trait("Name", "BrandedTitleMatchTests")]
    [Trait("Category", "Application")]
    public sealed class BrandedTitleMatchTests : BaseTests
    {
        private static bool Candidate(
            string postTitle, string postAuthor, string? postSeries,
            string bookTitle, string author, string? postAsin = null, string? bookAsin = null) =>
            SuggestionReleaseMatch.AbookPostMatchesAllowingBranding(
                postAsin, postTitle, postAuthor, postSeries, bookAsin, bookTitle, [author]);

        [Fact]
        public void TheBrandInTheCataloguesTitleMayComeFromThePostsAuthor()
        {
            Assert.True(Candidate(
                postTitle: "Red Winter",
                postAuthor: "Tom Clancy [with Marc Cameron]",
                postSeries: "Jack Ryan",
                bookTitle: "Tom Clancy Red Winter",
                author: "Marc Cameron"));
        }

        [Fact]
        public void APostMayNotBeABiggerThingThanTheBook()
        {
            // Sten book one against book eight's release, which is how the wrong
            // recording was imported as Sten.
            Assert.False(Candidate(
                postTitle: "Empires End",
                postAuthor: "Allan Cole and Chris Bunch",
                postSeries: "Sten",
                bookTitle: "Sten",
                author: "Chris Bunch"));
        }

        [Fact]
        public void AWordTheCatalogueAddedThatNobodyAccountsForIsNotBranding()
        {
            // "Devil's Desk 4" against the post for book one: the 4 is nowhere.
            Assert.False(Candidate(
                postTitle: "Devil's Desk",
                postAuthor: "Mark Tufo",
                postSeries: "Michael Talbot Adventures",
                bookTitle: "Devil's Desk 4",
                author: "Mark Tufo"));
        }

        [Fact]
        public void AnOrdinaryExactMatchStillMatches()
        {
            Assert.True(Candidate(
                postTitle: "Devils Desk 2",
                postAuthor: "Mark Tufo",
                postSeries: null,
                bookTitle: "Devils Desk 2",
                author: "Mark Tufo"));
        }

        [Fact]
        public void TheWrongAuthorIsStillTheWrongAuthor()
        {
            Assert.False(Candidate(
                postTitle: "Red Winter",
                postAuthor: "Somebody Else",
                postSeries: null,
                bookTitle: "Tom Clancy Red Winter",
                author: "Marc Cameron"));
        }

        [Fact]
        public void ADeclaredAsinStillDecidesOnItsOwn()
        {
            Assert.False(Candidate(
                postTitle: "Red Winter",
                postAuthor: "Tom Clancy [with Marc Cameron]",
                postSeries: "Jack Ryan",
                bookTitle: "Tom Clancy Red Winter",
                author: "Marc Cameron",
                postAsin: "B000OTHER0",
                bookAsin: "B09VMYFH24"));
        }
    }
}
