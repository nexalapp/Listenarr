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

namespace Listenarr.Tests.Features.Application.Audiobooks.Matching
{
    [Trait("Name", "ReleaseDateWindowTests")]
    [Trait("Category", "ReleaseDateWindow")]
    public class ReleaseDateWindowTests : BaseTests
    {
        [Theory]
        [InlineData("2028-01-11", 2028, 1, 11, ReleaseDatePrecision.Day)]
        [InlineData("2028-01-11T08:00:00Z", 2028, 1, 11, ReleaseDatePrecision.Day)]
        [InlineData("2028-01-11 08:00:00", 2028, 1, 11, ReleaseDatePrecision.Day)]
        [InlineData("2028-1-5", 2028, 1, 5, ReleaseDatePrecision.Day)]
        [InlineData("2028/01/11", 2028, 1, 11, ReleaseDatePrecision.Day)]
        [InlineData("  2028-01-11  ", 2028, 1, 11, ReleaseDatePrecision.Day)]
        public void TryParse_ReadsFullDates_AtDayPrecision(
            string value, int year, int month, int day, ReleaseDatePrecision expected)
        {
            // When
            var parsed = ReleaseDateWindow.TryParse(value, out var start, out var precision);

            // Then
            Assert.True(parsed);
            Assert.Equal(expected, precision);
            Assert.Equal(new DateOnly(year, month, day), start);
        }

        [Fact]
        public void TryParse_KeepsAMonthOnlyDate_AsAMonth()
        {
            // When a source commits only to a month, the window starts on the first
            // but the precision records that no day was ever given.
            var parsed = ReleaseDateWindow.TryParse("2028-03", out var start, out var precision);

            // Then
            Assert.True(parsed);
            Assert.Equal(ReleaseDatePrecision.Month, precision);
            Assert.Equal(new DateOnly(2028, 3, 1), start);
        }

        [Fact]
        public void TryParse_KeepsAYearOnlyDate_AsAYear()
        {
            // When
            var parsed = ReleaseDateWindow.TryParse("2028", out var start, out var precision);

            // Then
            Assert.True(parsed);
            Assert.Equal(ReleaseDatePrecision.Year, precision);
            Assert.Equal(new DateOnly(2028, 1, 1), start);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("soon")]
        [InlineData("28-01-11")]
        [InlineData("2028-13-01")]
        [InlineData("2028-02-30")]
        [InlineData("2028-00-05")]
        public void TryParse_RefusesToGuess_AtUnparseableInput(string? value)
        {
            // When
            var parsed = ReleaseDateWindow.TryParse(value, out _, out var precision);

            // Then
            Assert.False(parsed);
            Assert.Equal(ReleaseDatePrecision.None, precision);
        }

        [Fact]
        public void TryParse_AcceptsALeapDay_AndRejectsItInACommonYear()
        {
            Assert.True(ReleaseDateWindow.TryParse("2028-02-29", out var leap, out _));
            Assert.Equal(new DateOnly(2028, 2, 29), leap);
            Assert.False(ReleaseDateWindow.TryParse("2027-02-29", out _, out _));
        }

        [Theory]
        [InlineData("2028-01-11", true)]
        [InlineData("2026-09-01", true)]
        [InlineData("2026-08-31", false)]
        [InlineData("2026-08-30", false)]
        [InlineData("2020-05-05", false)]
        public void IsFutureRelease_ComparesAgainstTheGivenDay(string value, bool expected)
        {
            // Given a fixed "today" so the test does not drift
            var today = new DateOnly(2026, 8, 31);

            // Then a release on today itself is not still to come
            Assert.Equal(expected, ReleaseDateWindow.IsFutureRelease(value, today));
        }

        [Fact]
        public void IsFutureRelease_UsesTheStartOfTheWindow_SoAVaguePastDateIsNotAnnounced()
        {
            var today = new DateOnly(2026, 8, 31);

            // "2026" read in August 2026 could still mean December, but claiming the book is
            // unreleased would hide one the user can already go and get. The earliest day the
            // date could mean is January, which is past, so it is not announced.
            Assert.False(ReleaseDateWindow.IsFutureRelease("2026", today));
            Assert.False(ReleaseDateWindow.IsFutureRelease("2026-08", today));

            // A window that cannot possibly have started yet is announced.
            Assert.True(ReleaseDateWindow.IsFutureRelease("2026-09", today));
            Assert.True(ReleaseDateWindow.IsFutureRelease("2027", today));
        }

        [Fact]
        public void IsFutureRelease_IsFalse_WhenThereIsNoDate()
        {
            Assert.False(ReleaseDateWindow.IsFutureRelease(null, new DateOnly(2026, 8, 31)));
            Assert.False(ReleaseDateWindow.IsFutureRelease("not a date", new DateOnly(2026, 8, 31)));
        }
    }
}
