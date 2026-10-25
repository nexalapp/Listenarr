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
using Listenarr.Domain.Audiobooks.Quality;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks.Quality
{
    /// <summary>
    /// The probe against synthetic recordings whose defects are known by construction.
    /// Each known failure mode of a simpler measure gets its own regression: band energy
    /// reporting hiss as bandwidth, a varying production bed read as hiss, and codec
    /// noise that no level measure sees.
    /// </summary>
    [Trait("Name", "AudioQualityProbeTests")]
    [Trait("Category", "Domain")]
    public sealed class AudioQualityProbeTests : BaseTests
    {
        private const int SampleRate = 44_100;
        private const double WindowSeconds = 12;

        [Fact]
        public void CleanVoice_IsClean()
        {
            var report = Analyze(Windows(4, noiseDb: -90));

            Assert.Equal(AudioQuality.Clean, report.Verdict);
            Assert.True(report.Metrics.NoiseFloorDb < -60, $"floor {report.Metrics.NoiseFloorDb}");
            Assert.True(report.Metrics.SpeechBandwidthHz > 4000, $"bandwidth {report.Metrics.SpeechBandwidthHz}");
            Assert.NotNull(report.Metrics.Harmonicity);
            Assert.True(report.Metrics.Harmonicity > 0.6, $"harmonicity {report.Metrics.Harmonicity}");
            Assert.False(report.GenerativeRecommended);
            Assert.False(report.NeverPreDenoise);
        }

        [Fact]
        public void ConstantHiss_IsHiss()
        {
            var report = Analyze(Windows(4, noiseDb: -40));

            Assert.Equal(AudioQuality.Hiss, report.Verdict);
            Assert.InRange(report.Metrics.NoiseFloorDb, -58, -40);
        }

        /// <summary>
        /// The quiet-quartile trap: a narrator who pauses only briefly. The quietest quarter
        /// of frames still holds word tails, and its power mean is carried by them, so a
        /// clean studio reading measured −46 dB and was called hiss or a score. The floor is
        /// what the pauses hold, which the 5th percentile finds.
        /// </summary>
        [Fact]
        public void ContinuousNarration_FloorIsThePauses_NotTheWordTails()
        {
            var windows = Enumerable.Range(0, 10)
                .Select(i => Window(noiseDb: -90, seed: i, burstSeconds: 0.52, pauseSeconds: 0.08))
                .ToArray();

            var report = Analyze(windows);

            Assert.Equal(AudioQuality.Clean, report.Verdict);
            Assert.True(report.Metrics.NoiseFloorDb < -70, $"floor {report.Metrics.NoiseFloorDb}");
        }

        /// <summary>
        /// The energy-bandwidth trap: wideband hiss under a voice that stops at 3 kHz.
        /// Band energy reports the hiss ceiling as bandwidth; speech-above-floor must
        /// report the voice's own ceiling.
        /// </summary>
        [Fact]
        public void HissyMuffledVoice_BandwidthIsTheVoices_NotTheHisses()
        {
            var report = Analyze(Windows(4, noiseDb: -40, voiceTopHz: 3000));

            Assert.Equal(AudioQuality.HissAndMuffled, report.Verdict);
            Assert.True(
                report.Metrics.SpeechBandwidthHz <= 4000,
                $"bandwidth {report.Metrics.SpeechBandwidthHz} reported the hiss, not the voice");
        }

        [Fact]
        public void MuffledQuietVoice_IsMuffled_AndDarkEnoughForGenerative()
        {
            var report = Analyze(Windows(4, noiseDb: -90, voiceTopHz: 2000));

            Assert.Equal(AudioQuality.Muffled, report.Verdict);
            Assert.True(report.Metrics.BrightnessDeficitDb >= 25, $"deficit {report.Metrics.BrightnessDeficitDb}");
            Assert.True(report.GenerativeRecommended);
            Assert.False(report.NeverPreDenoise);
        }

        /// <summary>
        /// The stationarity rule: a background whose level moves window to window is a
        /// score, not the medium. Spectral flatness called exactly this hiss; the spread
        /// must not. The floor is pooled, so the quietest window decides whether the book
        /// is noisy at all: every window here is over the line.
        /// </summary>
        [Fact]
        public void VaryingBackground_IsProductionAudio_NotHiss()
        {
            var windows = new[]
            {
                Window(noiseDb: -26),
                Window(noiseDb: -44),
                Window(noiseDb: -34),
                Window(noiseDb: -48),
            };

            var report = Analyze(windows);

            Assert.Equal(AudioQuality.ProductionAudio, report.Verdict);
            Assert.False(report.GenerativeRecommended);
        }

        /// <summary>
        /// Codec damage is decided by the encode's bit rate: a 34 kbps file is damaged
        /// whatever it sounds like to a level measure, and the verdict must forbid
        /// pre-denoising.
        /// </summary>
        [Fact]
        public void StarvedEncode_IsCodecDamage()
        {
            var report = AudioQualityProbe.Analyze(Windows(4, noiseDb: -90), SampleRate, bitRate: 34_000);

            Assert.Equal(AudioQuality.CodecDamaged, report.Verdict);
            Assert.True(report.GenerativeRecommended);
            Assert.True(report.NeverPreDenoise);
        }

        /// <summary>
        /// Harmonicity alone must not call a book damaged. Calibrated within one recording,
        /// it did not separate narrators on the live library — a clean 130 kbps reading
        /// measured 0.45 against Ender's Shadow's 0.40 — and flagged every book probed.
        /// Low harmonicity at a healthy bit rate is still measured, and is not a verdict.
        /// </summary>
        [Fact]
        public void LowHarmonicityAtAHealthyBitRate_IsNotCodecDamage()
        {
            var report = AudioQualityProbe.Analyze(
                Windows(4, noiseDb: -90, voiceTopHz: 4500, inBandNoiseDb: -18), SampleRate, bitRate: 128_000);

            Assert.NotEqual(AudioQuality.CodecDamaged, report.Verdict);
            Assert.True(report.Metrics.Harmonicity < 0.55, $"harmonicity {report.Metrics.Harmonicity}");
            Assert.False(report.NeverPreDenoise);
        }

        [Fact]
        public void UnknownBitRate_IsNotCodecDamage()
        {
            var report = AudioQualityProbe.Analyze(Windows(4, noiseDb: -90), SampleRate, bitRate: null);

            Assert.NotEqual(AudioQuality.CodecDamaged, report.Verdict);
        }

        /// <summary>
        /// One rule decides a repair candidate, for the probe and for the library's filter.
        /// A clean but dark reading qualifies on its treble alone — the research's biggest
        /// group of generative wins — and a production never does, because the model
        /// destroys a score it takes for noise.
        /// </summary>
        [Theory]
        [InlineData(AudioQuality.CodecDamaged, 0, true)]
        [InlineData(AudioQuality.Clean, 30, true)]
        [InlineData(AudioQuality.Clean, 10, false)]
        [InlineData(AudioQuality.Hiss, 25, true)]
        [InlineData(AudioQuality.Hiss, 24.9, false)]
        [InlineData(AudioQuality.ProductionAudio, 40, false)]
        [InlineData(AudioQuality.Unknown, 40, false)]
        public void RepairRecommendation_FollowsOneRule(AudioQuality verdict, double deficitDb, bool expected)
        {
            Assert.Equal(expected, AudioRepairRecommendation.Of(verdict, deficitDb, offerThresholdDb: 25));
            var stored = $"{{\"NoiseFloorDb\":-70,\"BrightnessDeficitDb\":{deficitDb.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
            Assert.Equal(expected, AudioRepairRecommendation.Of(verdict, stored, offerThresholdDb: 25));
        }

        [Fact]
        public void RepairRecommendation_UnreadableMetrics_RecommendsOnlyCodecDamage()
        {
            Assert.False(AudioRepairRecommendation.Of(AudioQuality.Hiss, "not json", 25));
            Assert.False(AudioRepairRecommendation.Of(AudioQuality.Hiss, (string?)null, 25));
            Assert.True(AudioRepairRecommendation.Of(AudioQuality.CodecDamaged, (string?)null, 25));
        }

        [Fact]
        public void TooLittleAudio_IsUnknown()
        {
            var report = AudioQualityProbe.Analyze([new float[256]], SampleRate);
            Assert.Equal(AudioQuality.Unknown, report.Verdict);
        }

        private static AudioQualityReport Analyze(IReadOnlyList<float[]> windows) =>
            AudioQualityProbe.Analyze(windows, SampleRate);

        private static float[][] Windows(int count, double noiseDb, int voiceTopHz = 9000, double? inBandNoiseDb = null)
        {
            var windows = new float[count][];
            for (var i = 0; i < count; i++)
            {
                windows[i] = Window(noiseDb, voiceTopHz, inBandNoiseDb, seed: i);
            }

            return windows;
        }

        /// <summary>
        /// A voice-like window: a 140 Hz harmonic series with 1/f amplitude rolloff,
        /// spoken in bursts (0.35 s on, 0.25 s off by default, 10 ms cosine ramps so the gating
        /// itself adds no broadband splatter) over a white-noise floor.
        /// <paramref name="inBandNoiseDb"/> adds noise gated WITH the voice and limited
        /// to the voice's own band — the codec signature: energy between the harmonics,
        /// present only while there is signal to damage, invisible to any floor measure.
        /// </summary>
        private static float[] Window(
            double noiseDb,
            int voiceTopHz = 9000,
            double? inBandNoiseDb = null,
            int seed = 1,
            double burstSeconds = 0.35,
            double pauseSeconds = 0.25)
        {
            const double fundamental = 140;
            var length = (int)(WindowSeconds * SampleRate);
            var samples = new float[length];
            var random = new Random(37 + seed);
            var noiseAmp = Math.Pow(10, noiseDb / 20);

            var harmonics = new List<(double Freq, double Amp)>();
            for (var h = 1; h * fundamental < voiceTopHz; h++)
            {
                harmonics.Add((h * fundamental, 0.35 / h));
            }

            if (inBandNoiseDb != null)
            {
                // Two hundred random non-harmonic tones stand in for band-limited noise;
                // random frequencies so nothing about them repeats at the voice's period.
                var amp = Math.Pow(10, inBandNoiseDb.Value / 20) / Math.Sqrt(200);
                for (var i = 0; i < 200; i++)
                {
                    harmonics.Add((300 + random.NextDouble() * (voiceTopHz - 300), amp));
                }
            }

            var phases = harmonics.Select(_ => random.NextDouble() * 2 * Math.PI).ToArray();
            for (var i = 0; i < length; i++)
            {
                var t = (double)i / SampleRate;
                var cycle = t % (burstSeconds + pauseSeconds);
                // 10 ms raised-cosine edges on each burst.
                var envelope = cycle >= burstSeconds ? 0
                    : cycle < 0.01 ? 0.5 - 0.5 * Math.Cos(Math.PI * cycle / 0.01)
                    : cycle > burstSeconds - 0.01 ? 0.5 - 0.5 * Math.Cos(Math.PI * (burstSeconds - cycle) / 0.01)
                    : 1;
                double v = 0;
                if (envelope > 0)
                {
                    for (var h = 0; h < harmonics.Count; h++)
                    {
                        v += harmonics[h].Amp * Math.Sin(2 * Math.PI * harmonics[h].Freq * t + phases[h]);
                    }

                    v *= envelope;
                }

                v += noiseAmp * (random.NextDouble() * 2 - 1);
                samples[i] = (float)Math.Clamp(v * 0.5, -1, 1);
            }

            return samples;
        }
    }
}
