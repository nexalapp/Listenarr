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
    /// Whether a record's stated runtime agrees with how long its audio actually runs.
    ///
    /// <para>
    /// Two recordings of one novel say the same title and the same author, so the credits
    /// cannot tell them apart and the audit calls the wrong one a match. Their lengths
    /// differ by an hour. Every wrong edition found in this library by hand was found this
    /// way: 3001 on record as 408 minutes against 370 minutes of audio, Eaters of the Dead
    /// likewise. The length is the one property of a recording that is hard to state
    /// wrongly and impossible to fake.
    /// </para>
    /// <para>
    /// A gap has to clear both a proportion and an absolute floor. Openings, closing
    /// credits and a publisher's ident move a book by a minute or two either way, and a
    /// short story where that is a tenth of the whole is not evidence of anything.
    /// </para>
    /// </summary>
    public static class RuntimeAgreement
    {
        /// <summary>Below this share of the record's runtime, a gap is production noise.</summary>
        /// <remarks>
        /// Five per cent, because the case this rule exists for sits at nine: 3001 on
        /// record as 408 minutes against 370 minutes of audio. A shop states its own
        /// recording's length to the minute, so a same-recording gap is well under one
        /// per cent and the room between is all slack.
        /// </remarks>
        public const double Tolerance = 0.05;

        /// <summary>
        /// And below this many minutes, whatever the share. A short story where an
        /// opening ident is a twentieth of the whole is not evidence of anything.
        /// </summary>
        public const double FloorMinutes = 10;

        /// <summary>
        /// True when the two lengths are far enough apart to mean a different recording.
        /// False whenever either is unknown: silence is not evidence.
        /// </summary>
        public static bool Disagree(double? recordedMinutes, double? measuredMinutes)
        {
            if (recordedMinutes is not { } recorded || measuredMinutes is not { } measured)
            {
                return false;
            }

            if (recorded <= 0 || measured <= 0)
            {
                return false;
            }

            var gap = Math.Abs(recorded - measured);
            return gap > FloorMinutes && gap > recorded * Tolerance;
        }

        /// <summary>Both lengths in hours and minutes, for the sentence on the badge.</summary>
        public static string Describe(double recordedMinutes, double measuredMinutes) =>
            $"the record says {Clock(recordedMinutes)} and the audio runs {Clock(measuredMinutes)}";

        private static string Clock(double minutes)
        {
            var whole = (int)Math.Round(minutes);
            var hours = whole / 60;
            var rest = whole % 60;
            return hours == 0 ? $"{rest}m" : rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
        }
    }
}
