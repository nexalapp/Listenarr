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
    /// The shape one series' positions are written in, taken from every position the
    /// series has: how many digits the leading number is widened to, and whether the
    /// whole numbers carry a <c>.0</c>.
    /// </summary>
    /// <remarks>
    /// The decimal is not for sorting - <c>2</c> already sorts between <c>1.5</c> and
    /// <c>2.6</c> - it is for the eye. A series with a novella at 2.6 reads as
    /// <c>1.0, 1.5, 2.0, 2.6, 3.0</c> rather than a ragged <c>1, 1.5, 2, 2.6, 3</c>, and
    /// a series with no novella keeps its plain <c>1, 2, 3</c>. Like the width it is
    /// decided by the series, not the book, so a novella arriving later reshapes its
    /// siblings on their next rename.
    /// </remarks>
    public readonly record struct SeriesPositionStyle(int Width, bool Decimals)
    {
        public static readonly SeriesPositionStyle Plain = new(1, false);

        /// <summary>The style that fits every one of these positions.</summary>
        public static SeriesPositionStyle For(IEnumerable<string?>? positionsInSeries)
        {
            var style = Plain;
            foreach (var position in positionsInSeries ?? [])
            {
                style = style.Widen(position);
            }

            return style;
        }

        /// <summary>This style, widened to also fit one more position.</summary>
        public SeriesPositionStyle Widen(string? position) =>
            new(
                Math.Max(Width, SeriesNumberFormatting.LeadingDigitCount(position)),
                Decimals || SeriesNumberFormatting.HasDecimalPart(position));
    }

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
    /// and everything after it is carried through untouched, which is the one rule tha
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
        public static string? Pad(string? position, int width) =>
            Pad(position, new SeriesPositionStyle(width, false));

        /// <summary>
        /// One position written in its series' style: the leading number widened, and a
        /// whole number given a <c>.0</c> when the series carries decimals.
        /// </summary>
        public static string? Pad(string? position, SeriesPositionStyle style)
        {
            if (string.IsNullOrWhiteSpace(position))
            {
                return position;
            }

            var trimmed = position.Trim();
            var digits = LeadingDigitCount(trimmed);
            if (digits == 0)
            {
                return position;
            }

            // Only the leading run moves. "1.5" becomes "01.5" and "1-3" becomes "01-3",
            // so a novella still sorts beside the book it follows and an omnibus still
            // says which books it holds.
            var widen = digits < style.Width;
            // Only a bare whole number takes the decimal: "1-3" is a range, "2b" is
            // whatever the source meant, and both are carried through as they came.
            var decimalise = style.Decimals && digits == trimmed.Length;
            if (!widen && !decimalise)
            {
                return position;
            }

            var padded = widen
                ? string.Concat(new string('0', style.Width - digits), trimmed)
                : trimmed;
            return decimalise ? padded + ".0" : padded;
        }

        /// <summary>Whether the position is a number with a fractional part: "1.5", "07.5".</summary>
        internal static bool HasDecimalPart(string? position)
        {
            var span = (position ?? string.Empty).AsSpan().Trim();
            var digits = LeadingDigitCount(position);
            return digits > 0
                && span.Length > digits + 1
                && span[digits] == '.'
                && char.IsAsciiDigit(span[digits + 1]);
        }

        internal static int LeadingDigitCount(string? value)
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
