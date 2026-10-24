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
using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.AbookLink
{
    /// <summary>
    /// The forum prints the two most recent days by name, which is the whole reason
    /// this exists: "Today at 4:37 PM" has to become a moment, and it is only a moment
    /// relative to the day the page was read.
    /// </summary>
    [Trait("Name", "SmfTimestampTests")]
    [Trait("Category", "Application")]
    public sealed class SmfTimestampTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Parse_ReadsTodayAgainstTheDayThePageWasRead()
        {
            Assert.Equal(
                new DateTime(2026, 10, 23, 16, 37, 12, DateTimeKind.Unspecified),
                SmfTimestamp.Parse("stalkerama Today at 04:37:12 PM", Now));
        }

        [Fact]
        public void Parse_ReadsYesterday()
        {
            Assert.Equal(
                new DateTime(2026, 10, 22, 11, 2, 14, DateTimeKind.Unspecified),
                SmfTimestamp.Parse("Yesterday at 11:02:14 AM", Now));
        }

        [Fact]
        public void Parse_ReadsADateWrittenOutInFull()
        {
            Assert.Equal(
                new DateTime(2026, 10, 22, 22, 52, 16, DateTimeKind.Unspecified),
                SmfTimestamp.Parse("Last post October 22, 2026, 10:52:16 PM by Dutchman", Now));
        }

        [Theory]
        [InlineData("Jan 5, 2024, 08:30 AM", 2024, 1, 5, 8, 30, 0)]
        [InlineData("2026-10-22, 04:54:27 PM", 2026, 10, 22, 16, 54, 27)]
        [InlineData("October 22, 2026, 16:54:27", 2026, 10, 22, 16, 54, 27)]
        public void Parse_ReadsTheOtherShapesTheForumCanBeSetTo(
            string text, int year, int month, int day, int hour, int minute, int second)
        {
            Assert.Equal(
                new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified),
                SmfTimestamp.Parse(text, Now));
        }

        [Fact]
        public void Parse_WithADateAndNoTime_TakesTheStartOfTheDay()
        {
            Assert.Equal(
                new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Unspecified),
                SmfTimestamp.Parse("October 22, 2026", Now));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("by Dutchman")]
        [InlineData("Smarch 40, 2026, 10:00 PM")]
        [InlineData("February 30, 2026, 10:00 PM")]
        public void Parse_OfSomethingThatIsNotATime_SaysSo(string? text)
        {
            // A guessed date would sort a row to the top of "recently posted" for ever.
            Assert.Null(SmfTimestamp.Parse(text, Now));
        }
    }
}
