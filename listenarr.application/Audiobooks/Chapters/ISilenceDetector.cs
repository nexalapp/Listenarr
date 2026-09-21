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
using Listenarr.Domain.Audiobooks.Chapters;

namespace Listenarr.Application.Audiobooks.Chapters
{
    /// <summary>
    /// Finds every pause in an audio file: the stretches below a noise floor for at
    /// least a given length.
    ///
    /// <para>
    /// One decode of the whole file, which is minutes for a long book and the cheapest
    /// way to learn where a chapter might begin when the file says nothing. Whether a
    /// pause is a chapter is not this contract's business.
    /// </para>
    /// </summary>
    public interface ISilenceDetector
    {
        /// <param name="path">The file to listen through.</param>
        /// <param name="minimumLength">Pauses shorter than this are not reported.</param>
        /// <param name="cancellationToken">Stops the decode; the file is untouched.</param>
        /// <exception cref="SilenceDetectionException">The file could not be decoded, or no ffmpeg is installed.</exception>
        Task<IReadOnlyList<SilenceSpan>> DetectAsync(string path, TimeSpan minimumLength, CancellationToken cancellationToken = default);
    }

    /// <summary>The pauses could not be found: a decode failure, a timeout, or no decoder.</summary>
    public sealed class SilenceDetectionException(string message, Exception? inner = null) : Exception(message, inner);
}
