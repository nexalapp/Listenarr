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

namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>
    /// Reads the timestamps SMF prints beside a post.
    ///
    /// <para>
    /// The forum writes the day in words and the two most recent days by name:
    /// <c>Today at 04:34:53 PM</c>, <c>Yesterday at 11:02:14 AM</c>,
    /// <c>October 22, 2026, 04:54:27 PM</c>. It renders them in whatever offset the
    /// signed-in account is configured for, which we have no way to know, so what comes
    /// back here is the forum's wall clock taken at face value. It is used to order a
    /// list and to say "posted yesterday"; nothing turns on the hour being exact.
    /// </para>
    /// </summary>
    public static partial class SmfTimestamp
    {
        [GeneratedRegex(@"\b(Today|Yesterday)\b\s+at\s+(\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AP]\.?M\.?)?)",
            RegexOptions.IgnoreCase)]
        private static partial Regex Relative();

        [GeneratedRegex(@"\b([A-Z][a-z]+)\s+(\d{1,2}),\s*(\d{4}),?\s*(\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AP]\.?M\.?)?)?",
            RegexOptions.IgnoreCase)]
        private static partial Regex Absolute();

        [GeneratedRegex(@"\b(\d{4})-(\d{2})-(\d{2})[,\s]+(\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AP]\.?M\.?)?)?",
            RegexOptions.IgnoreCase)]
        private static partial Regex Iso();

        private static readonly string[] TimeFormats =
        [
            "h:mm:ss tt", "h:mm tt", "hh:mm:ss tt", "hh:mm tt", "H:mm:ss", "H:mm", "HH:mm:ss", "HH:mm"
        ];

        /// <summary>
        /// The moment the text names, or null when it names none. <paramref name="now"/>
        /// is what "today" is measured against.
        /// </summary>
        public static DateTime? Parse(string? text, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var relative = Relative().Match(text);
            if (relative.Success)
            {
                var day = relative.Groups[1].Value.Equals("Yesterday", StringComparison.OrdinalIgnoreCase)
                    ? now.Date.AddDays(-1)
                    : now.Date;
                return day + (ParseTime(relative.Groups[2].Value) ?? TimeSpan.Zero);
            }

            var iso = Iso().Match(text);
            if (iso.Success
                && int.TryParse(iso.Groups[1].Value, out var isoYear)
                && int.TryParse(iso.Groups[2].Value, out var isoMonth)
                && int.TryParse(iso.Groups[3].Value, out var isoDay)
                && isoMonth is >= 1 and <= 12
                && isoDay >= 1 && isoDay <= DateTime.DaysInMonth(isoYear, isoMonth))
            {
                return new DateTime(isoYear, isoMonth, isoDay, 0, 0, 0, DateTimeKind.Unspecified)
                    + (ParseTime(iso.Groups[4].Value) ?? TimeSpan.Zero);
            }

            var absolute = Absolute().Match(text);
            if (!absolute.Success
                || !TryParseMonth(absolute.Groups[1].Value, out var month)
                || !int.TryParse(absolute.Groups[2].Value, out var dayOfMonth)
                || !int.TryParse(absolute.Groups[3].Value, out var year)
                || year is < 1990 or > 2200
                || dayOfMonth < 1
                || dayOfMonth > DateTime.DaysInMonth(year, month))
            {
                return null;
            }

            return new DateTime(year, month, dayOfMonth, 0, 0, 0, DateTimeKind.Unspecified)
                + (ParseTime(absolute.Groups[4].Value) ?? TimeSpan.Zero);
        }

        /// <summary>
        /// The month a name stands for, written out or abbreviated. Each list is
        /// searched on its own: both carry a thirteenth, empty entry for calendars with
        /// a leap month, so an index into the two joined together is not a month number.
        /// </summary>
        private static bool TryParseMonth(string name, out int month)
        {
            var formats = CultureInfo.InvariantCulture.DateTimeFormat;
            foreach (var names in new[] { formats.MonthNames, formats.AbbreviatedMonthNames })
            {
                for (var index = 0; index < 12 && index < names.Length; index++)
                {
                    if (names[index].Length > 0
                        && string.Equals(names[index], name, StringComparison.OrdinalIgnoreCase))
                    {
                        month = index + 1;
                        return true;
                    }
                }
            }

            month = 0;
            return false;
        }

        private static TimeSpan? ParseTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var cleaned = value.Replace(".", string.Empty).Trim().ToUpperInvariant();
            return DateTime.TryParseExact(
                cleaned,
                TimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
                ? parsed.TimeOfDay
                : null;
        }
    }
}
