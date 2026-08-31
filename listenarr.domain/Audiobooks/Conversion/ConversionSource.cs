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

namespace Listenarr.Domain.Audiobooks.Conversion
{
    /// <summary>
    /// One input file to a conversion, with the stream facts the plan depends on.
    /// </summary>
    /// <param name="FullPath">Absolute path to the source file.</param>
    /// <param name="RelativePath">
    /// Path relative to the book's directory, when known. Ordering prefers it, because a
    /// book split across "Disc 1"/"Disc 2" subdirectories only sorts correctly when the
    /// directory component is part of the comparison.
    /// </param>
    /// <param name="Duration">Decoded duration, used to place chapter boundaries.</param>
    /// <param name="BitRate">Source bitrate in bits per second, when known.</param>
    /// <param name="SampleRate">Source sample rate in Hz, when known.</param>
    /// <param name="Channels">Source channel count, when known.</param>
    /// <param name="EmbeddedTitle">Embedded title tag, used as a chapter name when meaningful.</param>
    public sealed record ConversionSource(
        string FullPath,
        string? RelativePath,
        TimeSpan Duration,
        int? BitRate,
        int? SampleRate,
        int? Channels,
        string? EmbeddedTitle = null);

    /// <summary>
    /// One chapter in the output, in the order it will be written.
    /// </summary>
    public sealed record ConversionChapter(
        int Number,
        string Title,
        TimeSpan Start,
        TimeSpan End,
        string SourceFullPath)
    {
        public TimeSpan Duration => End - Start;
    }

    /// <summary>
    /// The complete, ordered description of one conversion. Everything here is decided
    /// before ffmpeg is invoked, so the decisions are testable without running an encoder.
    /// </summary>
    public sealed record ConversionPlan(
        IReadOnlyList<ConversionSource> OrderedSources,
        IReadOnlyList<ConversionChapter> Chapters,
        int TargetBitRate,
        int TargetSampleRate,
        int TargetChannels)
    {
        public TimeSpan TotalDuration =>
            Chapters.Count == 0 ? TimeSpan.Zero : Chapters[^1].End;
    }
}
