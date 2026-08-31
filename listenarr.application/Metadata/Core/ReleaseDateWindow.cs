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

using System.Globalization;
using System.Text.RegularExpressions;

namespace Listenarr.Application.Metadata.Core
{
    /// <summary>
    /// How much of a published date a metadata source actually committed to.
    /// Audible announces some titles as a month or a year, and treating those as a
    /// specific day invents information the publisher has not given.
    /// </summary>
    public enum ReleaseDatePrecision
    {
        None = 0,
        Year = 1,
        Month = 2,
        Day = 3
    }

    /// <summary>
    /// Parses the loosely-typed <c>PublishedDate</c> string into the window of time it
    /// actually covers. A year-only date is the whole year, not January 1st.
    /// </summary>
    public static class ReleaseDateWindow
    {
        private static readonly Regex DatePattern = new(
            @"^(?<year>\d{4})(?:[-/](?<month>\d{1,2})(?:[-/](?<day>\d{1,2}))?)?$",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(1));

        /// <summary>
        /// Extracts the earliest day the release could fall on, plus how precise the
        /// source was. Returns false for anything unparseable, which callers treat as
        /// "no date" rather than guessing.
        /// </summary>
        public static bool TryParse(string? value, out DateOnly start, out ReleaseDatePrecision precision)
        {
            start = default;
            precision = ReleaseDatePrecision.None;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // "2028-01-11T08:00:00Z" and "2028-01-11 08:00" both reduce to the date part.
            var trimmed = value.Trim();
            var separatorIndex = trimmed.IndexOfAny(new[] { 'T', 't', ' ' });
            if (separatorIndex > 0)
            {
                trimmed = trimmed[..separatorIndex];
            }

            var match = DatePattern.Match(trimmed);
            if (!match.Success)
            {
                return false;
            }

            var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
            if (year < 1 || year > 9999)
            {
                return false;
            }

            if (!match.Groups["month"].Success)
            {
                start = new DateOnly(year, 1, 1);
                precision = ReleaseDatePrecision.Year;
                return true;
            }

            var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            if (month < 1 || month > 12)
            {
                return false;
            }

            if (!match.Groups["day"].Success)
            {
                start = new DateOnly(year, month, 1);
                precision = ReleaseDatePrecision.Month;
                return true;
            }

            var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            if (day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                return false;
            }

            start = new DateOnly(year, month, day);
            precision = ReleaseDatePrecision.Day;
            return true;
        }

        /// <summary>
        /// True when even the earliest day the date could mean is still ahead of
        /// <paramref name="today"/>. Comparing against the start of the window rather
        /// than its end keeps a vague past date ("2026", read in December 2026) out of
        /// the announced bucket: claiming a book is unreleased when it is already out
        /// hides a book the user could actually go and get.
        /// </summary>
        public static bool IsFutureRelease(string? publishedDate, DateOnly today)
        {
            return TryParse(publishedDate, out var start, out var precision)
                   && precision != ReleaseDatePrecision.None
                   && start > today;
        }
    }
}
