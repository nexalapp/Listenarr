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
namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>
    /// Whether a book's audio is short enough of its stated runtime that part of it is
    /// probably not there.
    ///
    /// <para>
    /// This started as a wider rule that called any runtime gap a different recording of
    /// the book. That claim was not usable. Across the library it fired on 56 books, and
    /// of those 19 had <em>more</em> audio than the record claimed, 11 were within a tenth
    /// of it, and 14 more sat in the band where an abridgement and a slower reading cannot
    /// be told apart without listening to the whole thing. None of that is something to
    /// act on, and a check nobody can act on is noise however true it is.
    /// </para>
    /// <para>
    /// What survives is the part that names a real defect: a book missing a quarter or
    /// more of itself. Those files stop partway through, which is worth knowing four hours
    /// before finding out. The answer is given as a share so the reader can judge it -
    /// "59% missing" and "25% missing" are different problems.
    /// </para>
    /// <para>
    /// Audio longer than the record is never reported. Nothing is missing, and the record
    /// simply describes a shorter edition than the file.
    /// </para>
    /// </summary>
    public static class AudioCompleteness
    {
        /// <summary>
        /// How much of a book may be absent before it is worth saying. A quarter: below it
        /// live abridgements, differently paced readings and records whose runtime came
        /// from another edition, none of which mean a broken file.
        /// </summary>
        public const double MissingThreshold = 0.25;

        /// <summary>
        /// The share of the book that is not in the files, or null when the two lengths
        /// cannot be compared or the audio is not meaningfully short.
        /// </summary>
        public static double? MissingShare(double? recordedMinutes, double? measuredMinutes)
        {
            if (recordedMinutes is not { } recorded || measuredMinutes is not { } measured)
            {
                return null;
            }

            if (recorded <= 0 || measured <= 0 || measured >= recorded)
            {
                return null;
            }

            var missing = (recorded - measured) / recorded;
            return missing >= MissingThreshold ? missing : null;
        }

        /// <summary>The sentence on the badge, naming the share and both lengths.</summary>
        public static string Describe(double missingShare, double recordedMinutes, double measuredMinutes) =>
            $"about {Share(missingShare)} of this book is missing: it should run {Clock(recordedMinutes)} "
            + $"and the files run {Clock(measuredMinutes)}";

        // Written out rather than formatted as a percentage: "P0" puts a space before the
        // sign in the invariant culture, and the badge reads better without one.
        private static string Share(double fraction) =>
            $"{Math.Round(fraction * 100).ToString(System.Globalization.CultureInfo.InvariantCulture)}%";

        private static string Clock(double minutes)
        {
            var whole = (int)Math.Round(minutes);
            var hours = whole / 60;
            var rest = whole % 60;
            return hours == 0 ? $"{rest}m" : rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
        }
    }
}
