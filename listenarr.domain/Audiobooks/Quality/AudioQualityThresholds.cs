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
namespace Listenarr.Domain.Audiobooks.Quality
{
    /// <summary>
    /// Where the probe draws its lines. The defaults were measured against a 1,225-book
    /// library, by probing every book and checking the flagged ones by ear; they are not
    /// first principles. Change one only against listening evidence.
    /// </summary>
    public sealed record AudioQualityThresholds
    {
        public static AudioQualityThresholds Default { get; } = new();

        /// <summary>The ACX noise-floor limit: a background above this is audible hiss.</summary>
        public double NoiseFloorDb { get; init; } = -60;

        /// <summary>
        /// How much the floor may move between sampled windows and still be the medium.
        /// Beyond it the "floor" is production audio — a score comes and goes, hiss does
        /// not. Measured: genuine tape transfers sit under ~5 dB of spread, scored
        /// productions over ~7; this line is the production side of that gap, so a
        /// borderline book is called hiss rather than waved through.
        /// </summary>
        public double ProductionSpreadDb { get; init; } = 7;

        /// <summary>
        /// Speech standing clear of its own floor by less than this is not usable
        /// bandwidth (80th percentile over 5th, per band). Band energy alone is exactly wrong on a hissy transfer — the hiss
        /// reaches the tape's ceiling and reads as bandwidth.
        /// </summary>
        public double SpeechClearanceDb { get; init; } = 12;

        /// <summary>Speech clear of its floor only at or below this is a muffled book.</summary>
        public int MuffleWallHz { get; init; } = 4000;

        /// <summary>Treble (4–9 kHz) relative to speech on a naturally bright voice.</summary>
        public double NaturalBrightnessDb { get; init; } = -25;

        /// <summary>
        /// A brightness deficit beyond this is more treble than harmonics derived from
        /// the real signal can honestly add; only a generative model reaches natural.
        /// 45 of 1,225 books sat past this line.
        /// </summary>
        public double GenerativeDeficitDb { get; init; } = 25;

        /// <summary>
        /// An encode below this many bits per second is codec-damaged. The library's
        /// genuine case is Ender's Shadow at 34 kbps; crushing a clean reading audibly
        /// filled the gaps between its harmonics from 48 kbps down, and not above.
        /// </summary>
        public int CodecDamagedBitRate { get; init; } = 48_000;
    }
}
