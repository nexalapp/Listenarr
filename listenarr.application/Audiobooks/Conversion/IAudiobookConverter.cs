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
using Listenarr.Domain.Audiobooks.Conversion;

namespace Listenarr.Application.Audiobooks.Conversion
{
    /// <summary>
    /// Why a conversion could not produce a usable file. The kind decides whether the
    /// job is worth retrying, so it is part of the contract rather than a log detail.
    /// </summary>
    public enum ConversionFailureKind
    {
        None,

        /// <summary>No encoder is installed. Retrying changes nothing until one appears.</summary>
        EncoderUnavailable,

        /// <summary>A source file was unreadable or has since gone.</summary>
        SourceUnreadable,

        /// <summary>The encoder ran and failed. Usually the source, occasionally the target.</summary>
        EncodeFailed,

        /// <summary>The encoder reported success but the output does not hold up.</summary>
        OutputRejected,

        /// <summary>Something transient: disk pressure, a share dropping out mid-write.</summary>
        Transient,

        Unknown
    }

    /// <summary>
    /// Everything the encoder needs for one conversion. The output goes to a scratch
    /// path; publishing it into the library is the caller's job, so a failure here
    /// cannot touch the file the library is currently serving.
    /// </summary>
    public sealed record ConversionRequest(
        ConversionPlan Plan,
        string ScratchOutputPath,
        AudioMetadata Tags,
        string? CoverArtPath = null);

    /// <summary>Progress of an in-flight encode.</summary>
    public sealed record ConversionProgress(
        double Fraction,
        TimeSpan Encoded,
        TimeSpan Total);

    /// <summary>
    /// Outcome of one conversion attempt. <paramref name="Message"/> is written for an
    /// operator reading the Activity view, not for a log grep.
    /// </summary>
    public sealed record ConversionResult(
        bool Success,
        ConversionFailureKind FailureKind = ConversionFailureKind.None,
        string? Message = null,
        TimeSpan OutputDuration = default,
        int ChapterCount = 0)
    {
        public static ConversionResult Ok(TimeSpan duration, int chapterCount) =>
            new(true, ConversionFailureKind.None, null, duration, chapterCount);

        public static ConversionResult Fail(ConversionFailureKind kind, string message) =>
            new(false, kind, message);
    }

    /// <summary>
    /// Encodes an ordered set of audio files into one chaptered M4B.
    /// </summary>
    public interface IAudiobookConverter
    {
        /// <summary>
        /// Whether an encoder is available right now. Checked before a job is queued so
        /// a missing binary is reported as a refusal to start rather than a failed run.
        /// </summary>
        Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Run one conversion, reporting progress as it goes. Never throws for an
        /// expected failure; the outcome is in the returned result.
        /// </summary>
        Task<ConversionResult> ConvertAsync(
            ConversionRequest request,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
