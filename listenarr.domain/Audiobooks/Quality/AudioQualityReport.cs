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
    /// <summary>What the probe measured, kept so a verdict can be re-examined without decoding again.</summary>
    /// <param name="NoiseFloorDb">The background between words: median over windows of the quietest quarter of frames, 300 Hz–10 kHz.</param>
    /// <param name="FloorSpreadDb">How much that floor moves between windows. Null when too few windows were readable to say.</param>
    /// <param name="SpeechBandwidthHz">The highest band where speech still stands clear of its own floor.</param>
    /// <param name="BrightnessDb">Treble (4–9 kHz) minus speech level during speech; a natural voice sits near −25.</param>
    /// <param name="BrightnessDeficitDb">How far below natural the voice is; positive is darker.</param>
    /// <param name="Harmonicity">
    /// During voiced frames, how much of 0.5–1.5 kHz sits on the harmonic series rather
    /// than between it (0.72–0.74 on an undamaged recording). Null when nothing voiced
    /// was found. The codec-damage measure: quantisation noise fills the troughs between
    /// harmonics without changing any level.
    /// </param>
    /// <param name="WindowCount">How many sampled windows were analysed.</param>
    public sealed record AudioQualityMetrics(
        double NoiseFloorDb,
        double? FloorSpreadDb,
        int SpeechBandwidthHz,
        double BrightnessDb,
        double BrightnessDeficitDb,
        double? Harmonicity,
        int WindowCount);

    /// <summary>The probe's answer and the evidence behind it.</summary>
    /// <param name="Verdict">The verdict.</param>
    /// <param name="Reason">One sentence for the badge's tooltip.</param>
    /// <param name="Metrics">What was measured.</param>
    /// <param name="GenerativeRecommended">
    /// Whether the damage is past what cheap filtering repairs: a brightness deficit no
    /// honest harmonic exciter closes, or codec noise inside the voice itself.
    /// </param>
    /// <param name="NeverPreDenoise">
    /// Codec-damaged audio must not be denoised before a generative repair. The noise is
    /// correlated with the signal, so a denoiser classifies the speech as noise: measured
    /// full strength, it deleted 10–15 dB of the consonant bands and the generative model
    /// then invented replacements. Stored now so a later denoise chain cannot forget it.
    /// </param>
    public sealed record AudioQualityReport(
        AudioQuality Verdict,
        string Reason,
        AudioQualityMetrics Metrics,
        bool GenerativeRecommended,
        bool NeverPreDenoise);
}
