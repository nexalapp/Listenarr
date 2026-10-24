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
using Listenarr.Domain.Audiobooks.Conversion;

namespace Listenarr.Domain.Audiobooks.Repair
{
    /// <summary>
    /// The encode a repaired book gets: fixed, by design, and never the transcode
    /// policy's source-derived answer.
    ///
    /// <para>
    /// The transcode selectors clamp to what the sources carry, on the stated premise
    /// that a transcode never recovers detail the source encoder discarded. A repair is
    /// the one path where that premise is false: the generative model legitimately
    /// rebuilds treble the source never had. Measured cost of reusing the transcode
    /// policy anyway: a book restored to 16 kHz of audible bandwidth was encoded back at
    /// its source's rate and came out with 4 kHz — the whole repair discarded at the
    /// encode step, after days of CPU.
    /// </para>
    /// <para>
    /// The intermediates happen to be 44.1 kHz FLAC today, so the default selector would
    /// coincidentally pick the right rate — and a nonsense bitrate. The explicit policy
    /// exists so a future change to the intermediates cannot silently change the output.
    /// </para>
    /// </summary>
    public static class RepairPlanPolicy
    {
        public const int BitRate = 128_000;
        public const int SampleRate = 44_100;

        /// <summary>Channels still follow the source, capped at stereo — the one rule the repair keeps.</summary>
        public static ConversionTarget SelectTarget(IReadOnlyList<ConversionSource> sources) =>
            new(BitRate, SampleRate, ConversionPlanner.SelectChannels(sources));
    }
}
