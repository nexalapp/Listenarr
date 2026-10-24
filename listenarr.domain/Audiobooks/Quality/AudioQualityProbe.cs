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
    public static class AudioQualityProbe
    {
        /// <summary>
        /// Frames per the research tooling: 2048 samples, half-overlap. Harmonicity's
        /// calibration (clean 0.715, damaged 0.413) is bound to this resolution — a finer
        /// FFT resolves the troughs differently and the thresholds stop meaning anything.
        /// </summary>
        private const int Frame = 2048;
        private const int Hop = Frame / 2;

        /// <summary>Frames in the loudest 30% carry the voice; the quietest 25% are the background.</summary>
        private const double VoicedPercentile = 70;
        private const double QuietPercentile = 25;

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

            var floors = analysed.Select(s => s.FloorDb).OrderBy(f => f).ToList();
            var floorDb = floors[floors.Count / 2];
            double? spreadDb = floors.Count >= 3 ? StdDev(floors) : null;

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

        /// <summary>
        /// The highest band in which the loud frames stand clear of the quiet ones.
        ///
        /// Loud-over-quiet rather than band energy over anything absolute: hiss is in the
        /// loud frames and the quiet frames alike, so it cancels, and only frequencies the
        /// voice actually reaches show a margin.
        /// </summary>
        private static int SpeechBandwidth(List<WindowSpectra> windows, int sampleRate, double clearanceDb)
        {
            var top = (int)Math.Min(10_000, 0.45 * sampleRate);
            var best = 0;
            for (var lo = 300; lo < top; lo = (int)(lo * 1.25))
            {
                var hi = Math.Min((int)(lo * 1.25), top);
                var loud = MeanBandPower(windows, lo, hi, loudFrames: true);
                var quiet = MeanBandPower(windows, lo, hi, loudFrames: false);
                if (loud <= 0 || quiet <= 0)
                {
                    continue;
                }

                if (10 * Math.Log10(loud / quiet) >= clearanceDb)
                {
                    best = hi;
                }
            }

            return best;
        }

        private static double Brightness(List<WindowSpectra> windows, int sampleRate)
        {
            var speech = MeanBandPower(windows, 300, 3000, loudFrames: true);
            var trebleTop = (int)Math.Min(9000, 0.45 * sampleRate);
            var treble = trebleTop > 4000
                ? MeanBandPower(windows, 4000, trebleTop, loudFrames: true)
                : 0;
            if (speech <= 0)
            {
                return 0;
            }

            // A source whose rate cannot carry treble at all is simply very dark; −80 dB
            // stands in so the deficit arithmetic needs no special case.
            return treble <= 0 ? -80 : 10 * Math.Log10(treble / speech);
        }

        private static double MeanBandPower(List<WindowSpectra> windows, int loHz, int hiHz, bool loudFrames)
        {
            double sum = 0;
            long count = 0;
            foreach (var w in windows)
            {
                var (bandSum, bandCount) = w.BandPower(loHz, hiHz, loudFrames);
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

        private static double StdDev(IReadOnlyList<double> values)
        {
            var mean = values.Average();
            return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
        }

        /// <summary>One window's power spectra, framed and classified once, queried per measure.</summary>
        private sealed class WindowSpectra
        {
            private readonly double[][] _power;        // [frame][bin]
            private readonly bool[] _loud;
            private readonly bool[] _quiet;
            private readonly double _binHz;

            public double FloorDb { get; }

            private WindowSpectra(double[][] power, bool[] loud, bool[] quiet, double binHz, double floorDb)
            {
                _power = power;
                _loud = loud;
                _quiet = quiet;
                _binHz = binHz;
                FloorDb = floorDb;
            }

            public static WindowSpectra? Of(float[] samples, int sampleRate)
            {
                var frames = 1 + (samples.Length - Frame) / Hop;
                var hann = new double[Frame];
                double hannPower = 0;
                for (var i = 0; i < Frame; i++)
                {
                    hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Frame);
                    hannPower += hann[i] * hann[i];
                }

                hannPower /= Frame;

                var power = new double[frames][];
                var totals = new double[frames];
                var re = new double[Frame];
                var im = new double[Frame];
                // Normalised so a full-scale tone reads near 0 dB whatever the frame
                // size: |X|² / (N² · mean(hann²)), doubled off the one-sided fold.
                var scale = 2.0 / ((double)Frame * Frame * hannPower);
                for (var f = 0; f < frames; f++)
                {
                    var offset = f * Hop;
                    for (var i = 0; i < Frame; i++)
                    {
                        re[i] = samples[offset + i] * hann[i];
                        im[i] = 0;
                    }

                    RadixTwoFft.Transform(re, im);
                    var bins = new double[Frame / 2];
                    for (var b = 1; b < Frame / 2; b++)
                    {
                        bins[b] = (re[b] * re[b] + im[b] * im[b]) * scale;
                        totals[f] += bins[b];
                    }

                    power[f] = bins;
                }

                if (frames < 8)
                {
                    return null;
                }

                var loudCut = Percentile(totals, VoicedPercentile);
                var quietCut = Percentile(totals, QuietPercentile);
                var loud = new bool[frames];
                var quiet = new bool[frames];
                for (var f = 0; f < frames; f++)
                {
                    loud[f] = totals[f] >= loudCut;
                    quiet[f] = totals[f] <= quietCut;
                }

                var binHz = (double)sampleRate / Frame;
                var floorLo = (int)(300 / binHz);
                var floorHi = (int)Math.Min(10_000 / binHz, Frame / 2 - 1);
                double floorSum = 0;
                long floorFrames = 0;
                for (var f = 0; f < frames; f++)
                {
                    if (!quiet[f])
                    {
                        continue;
                    }

                    // The whole band's power per frame, not the per-bin mean: the ACX
                    // −60 dB line is about how loud the background IS, and a floor spread
                    // over a thousand bins reads ~30 dB quieter per bin than it sounds.
                    double frameBand = 0;
                    for (var b = floorLo; b <= floorHi; b++)
                    {
                        frameBand += power[f][b];
                    }

                    floorSum += frameBand;
                    floorFrames++;
                }

                var floorDb = floorFrames == 0
                    ? -120
                    : 10 * Math.Log10(Math.Max(floorSum / floorFrames, 1e-12));

                return new WindowSpectra(power, loud, quiet, binHz, floorDb);
            }

            public (double Sum, long Count) BandPower(int loHz, int hiHz, bool loudFrames)
            {
                var lo = Math.Max(1, (int)(loHz / _binHz));
                var hi = Math.Min((int)(hiHz / _binHz), _power[0].Length - 1);
                if (hi <= lo)
                {
                    return (0, 0);
                }

                var mask = loudFrames ? _loud : _quiet;
                double sum = 0;
                long count = 0;
                for (var f = 0; f < _power.Length; f++)
                {
                    if (!mask[f])
                    {
                        continue;
                    }

                    for (var b = lo; b <= hi; b++)
                    {
                        sum += _power[f][b];
                    }

                    count += hi - lo + 1;
                }

                return (sum, count);
            }

            public void HarmonicityScores(int loBin, int hiBin, int minLag, int maxLag, List<double> into)
            {
                var n = hiBin - loBin;
                var d = new double[n];
                for (var f = 0; f < _power.Length; f++)
                {
                    if (!_loud[f])
                    {
                        continue;
                    }

                    double mean = 0;
                    for (var b = 0; b < n; b++)
                    {
                        d[b] = 10 * Math.Log10(Math.Max(_power[f][loBin + b], 1e-20));
                        mean += d[b];
                    }

                    mean /= n;
                    double norm = 0;
                    for (var b = 0; b < n; b++)
                    {
                        d[b] -= mean;
                        norm += d[b] * d[b];
                    }

                    if (norm < 1e-9)
                    {
                        continue;
                    }

                    double best = 0;
                    for (var lag = minLag; lag <= Math.Min(maxLag, n - 1); lag++)
                    {
                        double ac = 0;
                        for (var b = 0; b + lag < n; b++)
                        {
                            ac += d[b] * d[b + lag];
                        }

                        best = Math.Max(best, ac / norm);
                    }

                    into.Add(best);
                }
            }

            private static double Percentile(double[] values, double percentile)
            {
                var sorted = (double[])values.Clone();
                Array.Sort(sorted);
                var index = (int)Math.Clamp(percentile / 100 * (sorted.Length - 1), 0, sorted.Length - 1);
                return sorted[index];
            }
        }
    }
}
