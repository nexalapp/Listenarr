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

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// Why a tag write could not produce a usable file. The kind decides whether the job
    /// is worth retrying, so it is part of the contract rather than a log detail.
    /// </summary>
    public enum TagWriteFailureKind
    {
        None,

        /// <summary>No ffmpeg is installed. Retrying changes nothing until one appears.</summary>
        WriterUnavailable,

        /// <summary>The file was unreadable, has gone, or is not a container tags can be written to.</summary>
        SourceUnreadable,

        /// <summary>ffmpeg ran and failed.</summary>
        WriteFailed,

        /// <summary>The rewrite finished but the result does not hold up when read back.</summary>
        OutputRejected,

        /// <summary>Something transient: disk pressure, a share dropping out mid-write.</summary>
        Transient,

        Unknown
    }

    /// <summary>What a file currently carries. Read once and used for both the plan and the verification.</summary>
    public sealed record AudiobookFileTags(
        IReadOnlyDictionary<string, string> Tags,
        int ChapterCount,
        TimeSpan Duration,
        bool HasCoverArt)
    {
        public static AudiobookFileTags Empty { get; } = new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            0,
            TimeSpan.Zero,
            false);
    }

    /// <summary>
    /// One file's rewrite. The output goes to a scratch path; publishing it into the
    /// library is the caller's job, so a failed write cannot replace the file the library
    /// is currently serving.
    /// </summary>
    /// <remarks>
    /// <c>Tags</c> is the complete set the output should carry, not a delta: the
    /// container's metadata is replaced rather than merged, which is what stops a second
    /// run accumulating a second copy of every tag. <c>CoverArtPath</c> is a cover to
    /// embed, or null to keep whatever art the file already has.
    /// </remarks>
    public sealed record TagWriteRequest(
        string SourcePath,
        string ScratchOutputPath,
        IReadOnlyDictionary<string, string> Tags,
        AudiobookFileTags Existing,
        string? CoverArtPath = null);

    /// <summary>
    /// Outcome of one rewrite. <paramref name="Message"/> is written for an operator
    /// reading the Activity view, not for a log grep.
    /// </summary>
    public sealed record TagWriteResult(
        bool Success,
        TagWriteFailureKind FailureKind = TagWriteFailureKind.None,
        string? Message = null,
        int TagsWritten = 0)
    {
        public static TagWriteResult Ok(int tagsWritten) =>
            new(true, TagWriteFailureKind.None, null, tagsWritten);

        public static TagWriteResult Fail(TagWriteFailureKind kind, string message) =>
            new(false, kind, message);
    }

    /// <summary>
    /// Reads and rewrites the metadata of one audio container.
    ///
    /// A rewrite never re-encodes: a 600MB file changed to carry a different blurb has to
    /// come back byte-identical in its audio, and re-encoding it would be both slow and
    /// lossy. Chapters and cover art are carried across explicitly, because ffmpeg drops
    /// both without complaint when they are not asked for.
    /// </summary>
    public interface IAudiobookTagWriter
    {
        /// <summary>
        /// Whether a writer is available right now. Checked before a job is queued so a
        /// missing binary is reported as a refusal to start rather than a failed run.
        /// </summary>
        Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

        /// <summary>Read one file's current tags, chapter count, duration and cover art.</summary>
        Task<AudiobookFileTags> ReadAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Write the request's tags into a copy of the source at its scratch path, then
        /// read that copy back and prove it carries what was asked for. Never throws for
        /// an expected failure; the outcome is in the returned result.
        /// </summary>
        Task<TagWriteResult> WriteAsync(
            TagWriteRequest request,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
