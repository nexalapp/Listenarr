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
namespace Listenarr.Application.Audiobooks.Quality
{
    /// <summary>Decoded mono audio from a handful of places in one file.</summary>
    /// <param name="Windows">One PCM block per readable window, in file order.</param>
    /// <param name="SampleRate">The decoded rate, which is the file's own.</param>
    public sealed record AudioSampleWindows(IReadOnlyList<float[]> Windows, int SampleRate);

    /// <summary>
    /// Decodes short windows spread through an audio file, for a probe that judges a
    /// ten-hour book from a couple of minutes of it.
    /// </summary>
    public interface IAudioSampleReader
    {
        /// <summary>
        /// Decode <paramref name="windowCount"/> windows of <paramref name="windowSeconds"/>,
        /// spread through the file's duration, to mono floats at the file's own rate.
        /// Returns null when the file cannot be decoded at all; windows that fail
        /// individually are simply absent. Throws <see cref="InvalidOperationException"/>
        /// when no decoder is installed — that is the environment's fault, not the
        /// file's, and must not be recorded against the book.
        /// </summary>
        Task<AudioSampleWindows?> ReadWindowsAsync(
            string path,
            double durationSeconds,
            int windowCount,
            double windowSeconds,
            CancellationToken cancellationToken = default);
    }
}
