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
    internal static class RadixTwoFft
    {
        /// <summary>
        /// In-place radix-2 FFT. Hand-rolled because the probe is this codebase's one
        /// FFT and a numerics package is a heavy way to buy forty lines.
        /// </summary>
        public static void Transform(double[] re, double[] im)
        {
            var n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                var bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                {
                    j ^= bit;
                }

                j |= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (var len = 2; len <= n; len <<= 1)
            {
                var angle = -2 * Math.PI / len;
                var wRe = Math.Cos(angle);
                var wIm = Math.Sin(angle);
                for (var i = 0; i < n; i += len)
                {
                    double curRe = 1, curIm = 0;
                    for (var k = 0; k < len / 2; k++)
                    {
                        var evenRe = re[i + k];
                        var evenIm = im[i + k];
                        var oddRe = re[i + k + len / 2] * curRe - im[i + k + len / 2] * curIm;
                        var oddIm = re[i + k + len / 2] * curIm + im[i + k + len / 2] * curRe;
                        re[i + k] = evenRe + oddRe;
                        im[i + k] = evenIm + oddIm;
                        re[i + k + len / 2] = evenRe - oddRe;
                        im[i + k + len / 2] = evenIm - oddIm;
                        (curRe, curIm) = (curRe * wRe - curIm * wIm, curRe * wIm + curIm * wRe);
                    }
                }
            }
        }
    }
}
