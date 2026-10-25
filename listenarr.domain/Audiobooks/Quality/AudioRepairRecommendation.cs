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

using System.Text.Json;

namespace Listenarr.Domain.Audiobooks.Quality
{
    /// <summary>
    /// Whether a probed book is worth a generative repair. One rule, used by the probe's own
    /// report and by every page that lists candidates, so they cannot disagree.
    /// </summary>
    public static class AudioRepairRecommendation
    {
        /// <summary>
        /// Codec damage always qualifies — nothing cheaper reaches it. Production audio never
        /// does: the model mistakes a score for noise and destroys it. Anything else
        /// qualifies when its voice is darker than natural by at least the offer
        /// threshold, whatever else the verdict says — a clean but dark reading is exactly
        /// the case the research found the model best at.
        /// </summary>
        public static bool Of(AudioQuality verdict, double brightnessDeficitDb, double offerThresholdDb) =>
            verdict switch
            {
                AudioQuality.Unknown or AudioQuality.ProductionAudio => false,
                AudioQuality.CodecDamaged => true,
                _ => brightnessDeficitDb >= offerThresholdDb
            };

        /// <summary>The same rule read from a stored verdict, whose metrics are kept as JSON.</summary>
        public static bool Of(AudioQuality verdict, string? metricsJson, double offerThresholdDb)
        {
            if (verdict is AudioQuality.Unknown or AudioQuality.ProductionAudio)
            {
                return false;
            }

            if (verdict == AudioQuality.CodecDamaged)
            {
                return true;
            }

            return DeficitOf(metricsJson) is { } deficit && deficit >= offerThresholdDb;
        }

        private static double? DeficitOf(string? metricsJson)
        {
            if (string.IsNullOrWhiteSpace(metricsJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(metricsJson);
                return document.RootElement.TryGetProperty(nameof(AudioQualityMetrics.BrightnessDeficitDb), out var value)
                    && value.TryGetDouble(out var deficit)
                    ? deficit
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
