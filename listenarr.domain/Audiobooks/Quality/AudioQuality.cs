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
    /// What a book's recording sounds like, as far as a few sampled minutes can tell.
    /// Judged from the audio alone — never from the container's claimed bitrate, which a
    /// cassette transfer encoded at 128k passes while remaining unlistenable.
    /// </summary>
    public enum AudioQuality
    {
        /// <summary>Not probed yet.</summary>
        Unknown,

        Clean,

        /// <summary>
        /// A constant noise floor above the −60 dB a listenable transfer keeps under.
        /// Constant is the tell: tape hiss belongs to the medium, so it is the same in
        /// chapter 2 and chapter 30, where a scored production's bed comes and goes.
        /// </summary>
        Hiss,

        /// <summary>
        /// Speech stops standing clear of its own floor at or below 4 kHz, so the
        /// fricatives that separate <i>s</i> from <i>f</i> are not in the file.
        /// </summary>
        Muffled,

        HissAndMuffled,

        /// <summary>
        /// A starved encode left quantisation noise between the harmonics of the voice,
        /// as loud as the speech it sits inside. No level measure sees it; harmonicity
        /// does. Denoising makes it worse — the noise is correlated with the signal, so
        /// a denoiser deletes the consonants along with it.
        /// </summary>
        CodecDamaged,

        /// <summary>
        /// The floor moves too much between sampled windows to be the medium: a score, a
        /// full-cast bed. Loud background, but not a defect and not repairable.
        /// </summary>
        ProductionAudio
    }

    /// <summary>The verdict as the API spells it, one word the client can filter on.</summary>
    public static class AudioQualityNames
    {
        public static string Of(AudioQuality quality) => quality switch
        {
            AudioQuality.Clean => "clean",
            AudioQuality.Hiss => "hiss",
            AudioQuality.Muffled => "muffled",
            AudioQuality.HissAndMuffled => "hiss-and-muffled",
            AudioQuality.CodecDamaged => "codec-damaged",
            AudioQuality.ProductionAudio => "production-audio",
            _ => "unknown"
        };
    }
}
