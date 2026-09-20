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
namespace Listenarr.Application.Audiobooks.Transcription
{
    /// <summary>
    /// What was heard in a stretch of audio. Empty text is a valid answer: music, silence,
    /// or a narrator who said nothing.
    /// </summary>
    /// <remarks>
    /// Whisper breaks speech into segments at pauses, and the pause is information: a
    /// chapter heading is its own segment, the prose that follows is the next. The
    /// segments are kept, one per line, so a parser can see the heading end where the
    /// narrator's breath did.
    /// </remarks>
    public sealed record Transcript(string Text)
    {
        public const char SegmentSeparator = '\n';

        public static Transcript Empty { get; } = new(string.Empty);

        public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

        public static Transcript FromSegments(IEnumerable<string> segments) =>
            new(string.Join(SegmentSeparator, segments.Select(segment => segment.Trim()).Where(segment => segment.Length > 0)));
    }

    /// <summary>
    /// Whether the transcription setting governs a request.
    ///
    /// <para>
    /// The setting exists because listening costs CPU minutes per book and a model
    /// download, and the chapter and audit features that listen to every scanned book
    /// respect it. A person clicking "Listen" has already decided that cost is worth
    /// it, and the Found tab promises to identify a book whose tags say nothing; both
    /// listen whatever the setting says.
    /// </para>
    /// </summary>
    public enum TranscriptionPolicy
    {
        WhenEnabled,
        Always,
    }

    /// <summary>
    /// Turns a short stretch of an audio file into text.
    ///
    /// <para>
    /// Deliberately narrow: a start and a length, never a whole book. Everything this app
    /// wants to hear is a few seconds long — the words after a chapter mark, the credits
    /// at the top of a file — and a contract that could transcribe nine hours would
    /// invite a job that does.
    /// </para>
    /// </summary>
    public interface ITranscriber
    {
        /// <summary>
        /// Whether transcription is switched on and the model is on disk. A model that is
        /// not starts downloading in the background and this answers false until it has
        /// landed: nothing that asks waits on a 150 MB download.
        /// </summary>
        Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

        /// <summary>As <see cref="IsAvailableAsync(CancellationToken)"/>, under the given policy.</summary>
        Task<bool> IsAvailableAsync(TranscriptionPolicy policy, CancellationToken cancellationToken = default);

        /// <summary>Where the named model stands — the configured one when null.</summary>
        Task<TranscriptionModelStatus> GetModelStatusAsync(string? model = null, CancellationToken cancellationToken = default);

        /// <summary>Start downloading the named model if it is not on disk. Returns at once; idempotent.</summary>
        Task<TranscriptionModelStatus> DownloadModelAsync(string model, CancellationToken cancellationToken = default);

        Task<Transcript> TranscribeAsync(
            string path,
            TimeSpan start,
            TimeSpan length,
            CancellationToken cancellationToken = default);

        /// <summary>As <see cref="TranscribeAsync(string, TimeSpan, TimeSpan, CancellationToken)"/>, under the given policy.</summary>
        Task<Transcript> TranscribeAsync(
            string path,
            TimeSpan start,
            TimeSpan length,
            TranscriptionPolicy policy,
            CancellationToken cancellationToken = default);
    }

    public enum TranscriptionModelState
    {
        Missing,
        Downloading,
        Ready,
        Failed
    }

    /// <summary>One model as the settings page shows it.</summary>
    public sealed record TranscriptionModelStatus(
        string Model,
        TranscriptionModelState State,
        long? SizeBytes,
        string? Error);

    /// <summary>
    /// Transcription was asked for while the model was not yet there. A moment, not a
    /// verdict: the job that hit it is worth retrying once the download lands.
    /// </summary>
    public sealed class TranscriptionUnavailableException(string message) : InvalidOperationException(message);
}
