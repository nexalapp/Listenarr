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
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Domain.FoundBooks;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.FoundBooks
{
    [Trait("Name", "FoundBookLibraryMatcherTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookLibraryMatcherTests : BaseTests
    {
        private static Audiobook Held(string title, string author, bool withFile = true, bool monitored = true, string? asin = null)
        {
            var book = new AudiobookBuilder().WithTitle(title).WithAuthor(author).WithMonitored(monitored).Build();
            book.Asin = asin;
            book.FilePath = withFile ? "/library/book.m4b" : null;
            return book;
        }

        [Fact]
        public void Asin_MatchesRegardlessOfTitle()
        {
            var library = new[] { Held("Wool", "Hugh Howey", asin: "B00ABCDEF1") };

            var match = FoundBookLibraryMatcher.Match("b00abcdef1", "Something Else", "Nobody", library);

            Assert.Equal(FoundBookLibraryStatus.InLibrary, match.Status);
        }

        [Fact]
        public void ExactTitleAndAuthor_IsInLibrary()
        {
            var library = new[] { Held("Revolt in 2100", "Robert A. Heinlein") };

            var match = FoundBookLibraryMatcher.Match(null, "Revolt in 2100", "Heinlein, Robert A", library);

            Assert.Equal(FoundBookLibraryStatus.InLibrary, match.Status);
            Assert.Equal(library[0].Id, match.AudiobookId);
        }

        [Fact]
        public void EditionWords_AreIgnoredInTheTitle()
        {
            var library = new[] { Held("The Shell Collector", "Hugh Howey") };

            Assert.Equal(
                FoundBookLibraryStatus.InLibrary,
                FoundBookLibraryMatcher.Match(null, "The Shell Collector (Unabridged)", "Hugh Howey", library).Status);
        }

        [Fact]
        public void ShortTitle_NeedsAnExactMatch()
        {
            // "Us" is contained in a great many titles; containment only counts for long ones.
            var library = new[] { Held("Us Against You", "Fredrik Backman") };

            Assert.Equal(
                FoundBookLibraryStatus.New,
                FoundBookLibraryMatcher.Match(null, "Us", "David Nicholls", library).Status);
        }

        [Fact]
        public void LongTitleContainment_MatchesWhenTheAuthorAgrees()
        {
            var library = new[] { Held("Foundation and Earth", "Isaac Asimov") };

            Assert.Equal(
                FoundBookLibraryStatus.InLibrary,
                FoundBookLibraryMatcher.Match(null, "Foundation and Earth: Foundation, Book 5", "Isaac Asimov", library).Status);
        }

        [Fact]
        public void SameTitleDifferentAuthor_IsNew()
        {
            var library = new[] { Held("The Explorer", "Someone Else") };

            Assert.Equal(
                FoundBookLibraryStatus.New,
                FoundBookLibraryMatcher.Match(null, "The Explorer", "James Smythe", library).Status);
        }

        [Fact]
        public void MonitoredWithoutAFile_IsWanted()
        {
            var library = new[] { Held("Columbus Day", "Craig Alanson", withFile: false, monitored: true) };

            var match = FoundBookLibraryMatcher.Match(null, "Columbus Day", "Craig Alanson", library);

            Assert.Equal(FoundBookLibraryStatus.Wanted, match.Status);
        }

        [Fact]
        public void UnmonitoredWithoutAFile_IsNew()
        {
            var library = new[] { Held("Columbus Day", "Craig Alanson", withFile: false, monitored: false) };

            Assert.Equal(FoundBookLibraryStatus.New, FoundBookLibraryMatcher.Match(null, "Columbus Day", "Craig Alanson", library).Status);
        }
    }
}
