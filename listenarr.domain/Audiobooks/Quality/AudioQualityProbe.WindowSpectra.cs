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
    public static partial class AudioQualityProbe
    {
        /// <summary>One window's power spectra, framed and classified once, queried per measure.</summary>
        private sealed class WindowSpectra
        {
            private readonly double[][] _power;        // [frame][bin]
            private readonly bool[] _loud;
            private readonly double _binHz;

            /// <summary>Each frame's whole-band power: the level the ACX floor is quoted against.</summary>
            public double[] FramePower { get; }

            /// <summary>This window's floor in the band where hiss lives, for stationarity.</summary>
            public double HissBandFloorDb { get; }

            private WindowSpectra(double[][] power, bool[] loud, double binHz, double[] framePower, double hissBandFloorDb)
            {
                _power = power;
                _loud = loud;
                _binHz = binHz;
                FramePower = framePower;
                HissBandFloorDb = hissBandFloorDb;
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
                var loud = new bool[frames];
                for (var f = 0; f < frames; f++)
                {
                    loud[f] = totals[f] >= loudCut;
                }

                var spectra = new WindowSpectra(power, loud, (double)sampleRate / Frame, totals, -120);
                var hissBand = spectra.FrameBandPower(HissBandLoHz, (int)Math.Min(HissBandHiHz, 0.45 * sampleRate));
                return hissBand.Length == 0
                    ? spectra
                    : new WindowSpectra(power, loud, spectra._binHz, totals, PowerDb(Percentile(hissBand, FloorPercentile)));
            }

            /// <summary>
            /// Each frame's summed power in a band, not the per-bin mean: a level is about
            /// how loud the band IS, and spread over a thousand bins a floor reads ~30 dB
            /// quieter per bin than it sounds. Empty when the band holds no bins.
            /// </summary>
            public double[] FrameBandPower(int loHz, int hiHz)
            {
                var (lo, hi) = Bins(loHz, hiHz);
                if (hi <= lo)
                {
                    return [];
                }

                var levels = new double[_power.Length];
                for (var f = 0; f < _power.Length; f++)
                {
                    for (var b = lo; b <= hi; b++)
                    {
                        levels[f] += _power[f][b];
                    }
                }

                return levels;
            }

            public (double Sum, long Count) VoicedBandPower(int loHz, int hiHz)
            {
                var (lo, hi) = Bins(loHz, hiHz);
                if (hi <= lo)
                {
                    return (0, 0);
                }

                double sum = 0;
                long count = 0;
                for (var f = 0; f < _power.Length; f++)
                {
                    if (!_loud[f])
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

            private (int Lo, int Hi) Bins(int loHz, int hiHz) =>
                (Math.Max(1, (int)(loHz / _binHz)), Math.Min((int)(hiHz / _binHz), _power[0].Length - 1));

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

            public static double Percentile(double[] values, double percentile)
            {
                var sorted = (double[])values.Clone();
                Array.Sort(sorted);
                var index = (int)Math.Clamp(percentile / 100 * (sorted.Length - 1), 0, sorted.Length - 1);
                return sorted[index];
            }
        }
    }
}
