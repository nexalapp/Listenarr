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
using Listenarr.Application.Audiobooks.Discover;
using Listenarr.Domain.Audiobooks.Discover;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Discover
{
    /// <summary>
    /// The stored selection is what the fetchers build URLs from, so what can get in
    /// here matters more than it looks. These pin the validation and the one thing a
    /// naive write would break: the other source's choice.
    /// </summary>
    [Trait("Name", "DiscoverSettingsTests")]
    [Trait("Category", "Application")]
    public sealed class DiscoverSettingsTests : BaseTests
    {
        [Fact]
        public void Read_OfNothingStored_FollowsNothing()
        {
            var selection = DiscoverSettings.Read(null, DiscoverSources.AudibleCharts);

            Assert.Empty(selection.Sections);
            Assert.Equal(DiscoverSettings.DefaultPagesPerSection, selection.PagesPerSection);
        }

        [Fact]
        public void Write_ThenRead_GivesBackWhatWasChosen()
        {
            var selection = new DiscoverSelection(
                [new DiscoverSelectedSection("18580606011", "Science Fiction & Fantasy", "science-fiction-fantasy-audiobooks")],
                3);

            var json = DiscoverSettings.Write(null, DiscoverSources.AudibleCharts, selection);
            var read = DiscoverSettings.Read(json, DiscoverSources.AudibleCharts);

            var section = Assert.Single(read.Sections);
            Assert.Equal("18580606011", section.Id);
            Assert.Equal("Science Fiction & Fantasy", section.Name);
            Assert.Equal("science-fiction-fantasy-audiobooks", section.Slug);
            Assert.Equal(3, read.PagesPerSection);
        }

        [Fact]
        public void Write_LeavesTheOtherSourcesChoiceAlone()
        {
            var json = DiscoverSettings.Write(
                null,
                DiscoverSources.AbookLink,
                new DiscoverSelection([new DiscoverSelectedSection("7", "Science Fiction")], 2));

            json = DiscoverSettings.Write(
                json,
                DiscoverSources.AudibleCharts,
                new DiscoverSelection([new DiscoverSelectedSection("18580606011", "Science Fiction & Fantasy")], 5));

            // Two tabs save in turn; the second must not erase the first.
            Assert.Equal("7", Assert.Single(DiscoverSettings.Read(json, DiscoverSources.AbookLink).Sections).Id);
            Assert.Equal(
                "18580606011",
                Assert.Single(DiscoverSettings.Read(json, DiscoverSources.AudibleCharts).Sections).Id);
        }

        [Theory]
        [InlineData("../../etc/passwd")]
        [InlineData("7;evil")]
        [InlineData("7/../9")]
        [InlineData("https://example.com/")]
        [InlineData("")]
        public void Sanitize_DropsAnythingThatIsNotAnId(string id)
        {
            // These become part of a URL. Anything but digits is refused here rather
            // than guarded at every place a request is built.
            var selection = DiscoverSettings.Sanitize(
                new DiscoverSelection([new DiscoverSelectedSection(id, "A board")], 5));

            Assert.Empty(selection.Sections);
        }

        [Fact]
        public void Sanitize_KeepsADigitsOnlyIdWithSurroundingSpace()
        {
            var selection = DiscoverSettings.Sanitize(
                new DiscoverSelection([new DiscoverSelectedSection(" 7 ", " Science   Fiction ")], 5));

            var section = Assert.Single(selection.Sections);
            Assert.Equal("7", section.Id);
            Assert.Equal("Science Fiction", section.Name);
        }

        [Fact]
        public void Sanitize_DropsASlugThatIsNotOne()
        {
            var selection = DiscoverSettings.Sanitize(
                new DiscoverSelection([new DiscoverSelectedSection("7", "A board", "../other/thing")], 5));

            Assert.Null(Assert.Single(selection.Sections).Slug);
        }

        [Fact]
        public void Sanitize_DropsDuplicatesAndKeepsTheFirst()
        {
            var selection = DiscoverSettings.Sanitize(new DiscoverSelection(
                [
                    new DiscoverSelectedSection("7", "Science Fiction"),
                    new DiscoverSelectedSection("7", "Science Fiction again")
                ],
                5));

            Assert.Equal("Science Fiction", Assert.Single(selection.Sections).Name);
        }

        [Fact]
        public void Sanitize_TakesTheControlCharactersOutOfAName()
        {
            var selection = DiscoverSettings.Sanitize(
                new DiscoverSelection([new DiscoverSelectedSection("7", "Science\nFiction\u0000")], 5));

            Assert.Equal("Science Fiction", Assert.Single(selection.Sections).Name);
        }

        [Fact]
        public void Sanitize_NamesASectionAfterItsIdWhenTheNameIsBlank()
        {
            var selection = DiscoverSettings.Sanitize(
                new DiscoverSelection([new DiscoverSelectedSection("7", "   ")], 5));

            Assert.Equal("7", Assert.Single(selection.Sections).Name);
        }

        [Theory]
        [InlineData(0, DiscoverSettings.DefaultPagesPerSection)]
        [InlineData(-4, DiscoverSettings.DefaultPagesPerSection)]
        [InlineData(1, 1)]
        [InlineData(9000, DiscoverSettings.MaxPagesPerSection)]
        public void Sanitize_HoldsThePageCountInRange(int requested, int expected)
        {
            var selection = DiscoverSettings.Sanitize(new DiscoverSelection([], requested));

            Assert.Equal(expected, selection.PagesPerSection);
        }

        [Fact]
        public void Read_OfSomethingThatIsNotJson_FollowsNothingRatherThanThrowing()
        {
            // The page has to load even when the column holds nonsense.
            Assert.Empty(DiscoverSettings.Read("{not json", DiscoverSources.AudibleCharts).Sections);
            Assert.Empty(DiscoverSettings.Read("[]", DiscoverSources.AudibleCharts).Sections);
        }

        [Fact]
        public void Write_ToAnUnknownSource_IsRefused()
        {
            Assert.Throws<ArgumentException>(() =>
                DiscoverSettings.Write(null, "goodreads", DiscoverSelection.Empty(5)));
        }
    }
}
