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

namespace Listenarr.Domain.FoundBooks
{
    /// <summary>
    /// Reads a running time out of the prose that comes with a rip: a comment tag, an
    /// .nfo, a release note. "Length: 11 hrs and 58 mins", "6h,2m", "Runtime: 09:14:02".
    /// </summary>
    public static partial class DeclaredLengthParser
    {
        [GeneratedRegex(
            @"\b(\d{1,2})\s*(?:hrs?|hours?|h)\b[\s,]*(?:and\s*)?(?:(\d{1,2})\s*(?:mins?|minutes?|m)\b)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex HoursMinutes();

        [GeneratedRegex(
            @"\b(?:length|runtime|run\s*time|duration|total\s*time)\s*[:=]?\s*(\d{1,2}):(\d{2}):(\d{2})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex LabelledClock();

        [GeneratedRegex(
            @"\b(\d{1,2})\s*(?:mins?|minutes?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex MinutesOnly();

        /// <summary>
        /// The first running time the text declares, in seconds, or null when it
        /// declares none. Anything under twenty minutes is ignored: that is a chapter
        /// length or a sample, not a book.
        /// </summary>
        public static double? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var clock = LabelledClock().Match(text);
            if (clock.Success)
            {
                var seconds = Int(clock.Groups[1]) * 3600 + Int(clock.Groups[2]) * 60 + Int(clock.Groups[3]);
                return Accept(seconds);
            }

            var hm = HoursMinutes().Match(text);
            if (hm.Success)
            {
                var minutes = hm.Groups[2].Success ? Int(hm.Groups[2]) : 0;
                return Accept(Int(hm.Groups[1]) * 3600 + minutes * 60);
            }

            var m = MinutesOnly().Match(text);
            if (m.Success)
            {
                return Accept(Int(m.Groups[1]) * 60);
            }

            return null;
        }

        private static int Int(Group group) =>
            int.Parse(group.Value, NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static double? Accept(int seconds) => seconds >= 20 * 60 ? seconds : null;
    }
}
