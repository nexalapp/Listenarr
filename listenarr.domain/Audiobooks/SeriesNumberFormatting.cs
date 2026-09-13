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

namespace Listenarr.Domain.Audiobooks
{
    /// <summary>
    /// Widening a series position so that a plain string sort puts book 2 before book 10.
    /// </summary>
    /// <remarks>
    /// A position is a string because it has to be: a library of 658 series books holds
    /// 627 plain integers, 24 decimals for the novellas that sit between them, and 7
    /// ranges for omnibus editions. An integer column loses a third of those and a
    /// floating-point one still cannot say <c>1-3</c>.
    /// <para>
    /// Nothing downstream sorts that correctly on its own. Folder listings, Plex's album
    /// sort tag and every plain string comparison order character by character, so
    /// <c>10</c> lands before <c>2</c>. Padding is the remedy, and it has to be applied
    /// without understanding the whole string: only the leading run of digits is widened
    /// and everything after it is carried through untouched, which is the one rule that
    /// works for all four shapes at once.
    /// </para>
    /// </remarks>
    public static class SeriesNumberFormatting
    {
        /// <summary>How many digits the longest whole number in a series needs.</summary>
        /// <remarks>
        /// Taken from the series rather than fixed, because that is what makes padding
        /// invisible where it is not needed: a trilogy keeps <c>1</c>, <c>2</c>, <c>3</c>
        /// and only a series that actually reaches ten starts writing <c>01</c>.
        /// </remarks>
        public static int WidthFor(IEnumerable<string?>? positionsInSeries)
        {
            var widest = 1;
            foreach (var position in positionsInSeries ?? [])
            {
                var digits = LeadingDigitCount(position);
                if (digits > widest)
                {
                    widest = digits;
                }
            }

            return widest;
        }

        /// <summary>
        /// One position widened to <paramref name="width"/> digits, or returned as it came
        /// when there is no leading number to widen.
        /// </summary>
        public static string? Pad(string? position, int width)
        {
            if (string.IsNullOrWhiteSpace(position) || width <= 1)
            {
                return position;
            }

            var trimmed = position.TrimStart();
            var digits = LeadingDigitCount(trimmed);
            if (digits == 0 || digits >= width)
            {
                return position;
            }

            // Only the leading run moves. "1.5" becomes "01.5" and "1-3" becomes "01-3",
            // so a novella still sorts beside the book it follows and an omnibus still
            // says which books it holds.
            return string.Concat(
                new string('0', width - digits),
                trimmed);
        }

        private static int LeadingDigitCount(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            var span = value.AsSpan().TrimStart();
            var digits = 0;
            while (digits < span.Length
                && char.IsDigit(span[digits])
                && char.IsAscii(span[digits]))
            {
                digits++;
            }

            return digits;
        }

        /// <summary>
        /// The series key two positions must share before their widths are compared.
        /// </summary>
        public static string SeriesKey(string? series) =>
            (series ?? string.Empty).Trim().ToLower(CultureInfo.InvariantCulture);
    }
}
