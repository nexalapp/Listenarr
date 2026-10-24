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
    /// Judges a recording from a few sampled windows of decoded audio. Pure: PCM in,
    /// verdict out, so every line of it is testable against synthetic signals.
    ///
    /// <para>
    /// Three independent defects, each with its own measure because each one defeated a
    /// simpler measure first:
    /// </para>
    /// <para>
    /// <b>Hiss</b> is a loudness, but only a <i>stationary</i> one — the floor is taken
    /// per window and its spread separates tape hiss from a production's score, which
    /// spectral flatness confidently failed to do (real hiss 0.001 against a scored
    /// production's 0.474, both wrong sides of any line).
    /// </para>
    /// <para>
    /// <b>Muffle</b> is bandwidth, but speech-above-floor bandwidth — on a hissy transfer
    /// the hiss reaches the tape's ceiling, and a band-energy measure reports that
    /// ceiling as the recording's bandwidth, exactly backwards.
    /// </para>
    /// <para>
    /// <b>Codec damage</b> is neither: a starved encoder leaves quantisation noise
    /// between the harmonics of the voice, as loud as the speech around it, invisible to
    /// every level measure. Harmonicity — autocorrelation of the log-spectrum across
    /// frequency during voiced frames — sees the troughs filled in.
    /// </para>
    /// </summary>
    public static partial class AudioQualityProbe
    {
        /// <summary>
        /// Frames per the research tooling: 2048 samples, half-overlap. Harmonicity's
        /// calibration (clean 0.715, damaged 0.413) is bound to this resolution — a finer
        /// FFT resolves the troughs differently and the thresholds stop meaning anything.
        /// </summary>
        private const int Frame = 2048;
        private const int Hop = Frame / 2;

        /// <summary>
        /// The noise floor is the 5th percentile of frame level, the research's measure. The
        /// quietest quarter was tried and is wrong: in continuous narration it still holds
        /// word tails and breaths, and a power mean over it is carried by them. Deployed, it
        /// put 667 of 1,386 books over the −60 dB line, where the 5th percentile put 142 of
        /// 1,225.
        /// </summary>
        private const double FloorPercentile = 5;

        /// <summary>Frames in the loudest 30% carry the voice.</summary>
        private const double VoicedPercentile = 70;

        /// <summary>A band's speech level is the 80th percentile of its frames.</summary>
        private const double SpeechPercentile = 80;

        /// <summary>A band more than this far under the loudest one is a trace, not bandwidth.</summary>
        private const double TraceBelowLoudestDb = 30;

        /// <summary>
        /// Stationarity is judged on 2–6 kHz, where hiss lives, so a loud low-frequency
        /// scene rumble cannot stand in for the floor.
        /// </summary>
        private const int HissBandLoHz = 2000;
        private const int HissBandHiHz = 6000;

        /// <param name="windows">Decoded mono windows of the file.</param>
        /// <param name="sampleRate">Their sample rate.</param>
        /// <param name="bitRate">The file's encoded bit rate, when known — what decides codec damage.</param>
        /// <param name="thresholds">Where the lines are drawn; the research defaults when null.</param>
        public static AudioQualityReport Analyze(
            IReadOnlyList<float[]> windows,
            int sampleRate,
            int? bitRate = null,
            AudioQualityThresholds? thresholds = null)
        {
            var t = thresholds ?? AudioQualityThresholds.Default;

            var analysed = windows
                .Where(w => w != null && w.Length >= Frame * 4)
                .Select(w => WindowSpectra.Of(w, sampleRate))
                .Where(s => s != null)
                .Select(s => s!)
                .ToList();

            if (analysed.Count == 0 || sampleRate <= 0)
            {
                return new AudioQualityReport(
                    AudioQuality.Unknown,
                    "Too little audio could be decoded to judge.",
                    new AudioQualityMetrics(0, null, 0, 0, 0, null, 0),
                    GenerativeRecommended: false,
                    NeverPreDenoise: false);
            }

            // Pooled, not per window then averaged: a narration pauses far more than 5% of
            // the time across the book, but one window can be a breathless passage.
            var floorDb = PowerDb(WindowSpectra.Percentile(
                analysed.SelectMany(s => s.FramePower).ToArray(), FloorPercentile));
            var bandFloors = analysed.Select(s => s.HissBandFloorDb).ToList();
            double? spreadDb = bandFloors.Count >= 3 ? StdDev(bandFloors) : null;

            var bandwidthHz = SpeechBandwidth(analysed, sampleRate, t.SpeechClearanceDb);
            var brightnessDb = Brightness(analysed, sampleRate);
            var deficitDb = t.NaturalBrightnessDb - brightnessDb;
            var harmonicity = Harmonicity(analysed, sampleRate);

            var metrics = new AudioQualityMetrics(
                Math.Round(floorDb, 1),
                spreadDb == null ? null : Math.Round(spreadDb.Value, 1),
                bandwidthHz,
                Math.Round(brightnessDb, 1),
                Math.Round(deficitDb, 1),
                harmonicity == null ? null : Math.Round(harmonicity.Value, 3),
                analysed.Count);

            return Judge(metrics, bitRate, t);
        }

        private static AudioQualityReport Judge(AudioQualityMetrics m, int? bitRate, AudioQualityThresholds t)
        {
            var report = JudgeVerdict(m, bitRate, t);
            return report with
            {
                GenerativeRecommended = AudioRepairRecommendation.Of(
                    report.Verdict, m.BrightnessDeficitDb, t.GenerativeDeficitDb)
            };
        }

        private static AudioQualityReport JudgeVerdict(AudioQualityMetrics m, int? bitRate, AudioQualityThresholds t)
        {
            // The encode's bit rate, not harmonicity, decides codec damage. Harmonicity was
            // calibrated within one recording against crushed copies of itself, and across
            // narrators it does not separate: on the live library a clean 130 kbps reading
            // measured 0.45 and the 34 kbps Ender's Shadow 0.40. It stays a metric.
            var codec = bitRate is > 0 && bitRate < t.CodecDamagedBitRate;
            var noisy = m.NoiseFloorDb > t.NoiseFloorDb;
            var production = noisy && m.FloorSpreadDb != null && m.FloorSpreadDb > t.ProductionSpreadDb;
            var hiss = noisy && !production;
            var muffled = m.SpeechBandwidthHz <= t.MuffleWallHz;

            if (codec)
            {
                // Codec damage usually brings a bandwidth wall with it; the wall is the
                // lesser finding, so one verdict carries both.
                return new AudioQualityReport(
                    AudioQuality.CodecDamaged,
                    $"Encoded at {bitRate / 1000} kbps, too few bits for speech: the encoder leaves its noise " +
                    "between the voice's harmonics. A denoiser would delete the consonants along with it.",
                    m, GenerativeRecommended: false, NeverPreDenoise: true);
            }

            if (production)
            {
                return new AudioQualityReport(
                    AudioQuality.ProductionAudio,
                    $"The background moves {m.FloorSpreadDb:0.#} dB between sampled windows — a score or " +
                    "production bed, not a defect.",
                    m, GenerativeRecommended: false, NeverPreDenoise: false);
            }

            if (hiss && muffled)
            {
                return new AudioQualityReport(
                    AudioQuality.HissAndMuffled,
                    $"A constant {m.NoiseFloorDb:0} dB floor and speech clear of it only to " +
                    $"{Khz(m.SpeechBandwidthHz)}.",
                    m, GenerativeRecommended: false, NeverPreDenoise: false);
            }

            if (hiss)
            {
                return new AudioQualityReport(
                    AudioQuality.Hiss,
                    $"A constant {m.NoiseFloorDb:0} dB background; a listenable transfer keeps under {t.NoiseFloorDb:0} dB.",
                    m, GenerativeRecommended: false, NeverPreDenoise: false);
            }

            if (muffled)
            {
                return new AudioQualityReport(
                    AudioQuality.Muffled,
                    $"Speech stands clear of its floor only to {Khz(m.SpeechBandwidthHz)}, so the fricatives " +
                    $"that separate 's' from 'f' are not in the file.",
                    m, GenerativeRecommended: false, NeverPreDenoise: false);
            }

            return new AudioQualityReport(
                AudioQuality.Clean,
                $"Floor {m.NoiseFloorDb:0} dB, speech clear to {Khz(m.SpeechBandwidthHz)}.",
                m, GenerativeRecommended: false, NeverPreDenoise: false);
        }

        private static string Khz(int hz) => $"{hz / 1000.0:0.#} kHz";

        /// <summary>Lower edges of the bands bandwidth is judged in; each spans edge to 1.4 × edge.</summary>
        private static readonly int[] BandEdgesHz = [1000, 2000, 4000, 6000, 8000, 10_000, 12_000, 14_000, 16_000, 18_000];

        /// <summary>
        /// The lower edge of the highest band in which speech stands clear of that band's
        /// own floor and is not a mere trace beside the loudest band — the research's measure.
        ///
        /// <para>
        /// Each band's level is a distribution over frames: its 80th percentile is the
        /// speech, its 5th the floor. Hiss is in both, so it cancels, and only frequencies
        /// the voice reaches show a margin. Band energy alone is exactly wrong on a hissy
        /// transfer: the hiss reaches the tape's ceiling and reads as bandwidth.
        /// </para>
        /// <para>
        /// The trace test matters as much as the margin. A dark transfer's voice still
        /// clears its floor at 8 kHz by a few faint harmonics, 40 dB under the vowels;
        /// without the test such books read as full-band: 30 of the library came out
        /// muffled where the research's 1,225-book pass found 108.
        /// </para>
        /// </summary>
        private static int SpeechBandwidth(List<WindowSpectra> windows, int sampleRate, double clearanceDb)
        {
            var bands = new List<(int Edge, double SpeechDb, double FloorDb)>();
            foreach (var edge in BandEdgesHz)
            {
                if (edge >= 0.45 * sampleRate)
                {
                    break;
                }

                var hi = (int)Math.Min(edge * 1.4, 0.5 * sampleRate);
                var levels = windows.SelectMany(w => w.FrameBandPower(edge, hi)).ToArray();
                if (levels.Length == 0)
                {
                    continue;
                }

                bands.Add((
                    edge,
                    PowerDb(WindowSpectra.Percentile(levels, SpeechPercentile)),
                    PowerDb(WindowSpectra.Percentile(levels, FloorPercentile))));
            }

            if (bands.Count == 0)
            {
                return 0;
            }

            var loudestDb = bands.Max(b => b.SpeechDb);
            return bands
                .Where(b => b.SpeechDb - b.FloorDb >= clearanceDb && b.SpeechDb > loudestDb - TraceBelowLoudestDb)
                .Select(b => b.Edge)
                .DefaultIfEmpty(0)
                .Max();
        }

        private static double Brightness(List<WindowSpectra> windows, int sampleRate)
        {
            var speech = MeanBandPower(windows, 300, 3000);
            var trebleTop = (int)Math.Min(9000, 0.45 * sampleRate);
            var treble = trebleTop > 4000
                ? MeanBandPower(windows, 4000, trebleTop)
                : 0;
            if (speech <= 0)
            {
                return 0;
            }

            // A source whose rate cannot carry treble at all is simply very dark; −80 dB
            // stands in so the deficit arithmetic needs no special case.
            return treble <= 0 ? -80 : 10 * Math.Log10(treble / speech);
        }

        private static double MeanBandPower(List<WindowSpectra> windows, int loHz, int hiHz)
        {
            double sum = 0;
            long count = 0;
            foreach (var w in windows)
            {
                var (bandSum, bandCount) = w.VoicedBandPower(loHz, hiHz);
                sum += bandSum;
                count += bandCount;
            }

            return count == 0 ? 0 : sum / count;
        }

        /// <summary>
        /// How much of 0.5–1.5 kHz sits on the harmonic series during voiced frames: the
        /// log-spectrum across that band is autocorrelated across frequency, and the peak
        /// over lags of 80–320 Hz — the range of a human fundamental — is the score.
        /// Quantisation noise fills the troughs between harmonics, flattening the comb.
        /// </summary>
        private static double? Harmonicity(List<WindowSpectra> windows, int sampleRate)
        {
            var binHz = (double)sampleRate / Frame;
            var loBin = (int)Math.Ceiling(500 / binHz);
            var hiBin = (int)Math.Min(1500 / binHz, Frame / 2);
            var minLag = Math.Max(1, (int)(80 / binHz));
            var maxLag = (int)(320 / binHz);
            if (hiBin - loBin < 16 || maxLag <= minLag)
            {
                return null;
            }

            var scores = new List<double>();
            foreach (var w in windows)
            {
                w.HarmonicityScores(loBin, hiBin, minLag, maxLag, scores);
            }

            return scores.Count == 0 ? null : scores.Average();
        }

        /// <summary>Digital silence reads as −120 dB rather than −∞.</summary>
        private static double PowerDb(double power) => Math.Max(10 * Math.Log10(Math.Max(power, 1e-12)), -120);

        private static double StdDev(IReadOnlyList<double> values)
        {
            var mean = values.Average();
            return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
        }
    }
}
