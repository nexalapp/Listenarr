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

using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Audiobooks.Chapters;

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// One audio file as the tag table shows it: what the file actually carries, and what
    /// Listenarr would put there.
    /// </summary>
    /// <remarks>
    /// <c>Tags</c> holds only the values the file really has, read back through the same
    /// probe the preview and the write-verification use — a row is evidence about the
    /// file, not a rendering of the database. <c>Expected</c> holds the planner's value
    /// for every tag whose mapping is on, so <c>Mismatched</c> can name the tags where
    /// the two disagree without the caller re-deriving anything.
    /// <para>
    /// <c>Writable</c> is false for the library's MP3 books. They are listed anyway,
    /// because "which books still carry ID3 and need converting" is exactly the question
    /// a tag table is opened to answer; the row simply cannot be edited in place.
    /// </para>
    /// <para>
    /// The path travels beside the tags because it is the same kind of fact: a value on
    /// disk that either agrees with what Listenarr would produce or does not. A file
    /// whose tags are perfect and whose folder predates the current naming pattern is
    /// wrong in a way a tag table that only knew about tags could not show.
    /// <c>ExpectedPath</c> is null when organizing could not answer for this book, which
    /// is not the same as "already correct" and must not be shown as agreement.
    /// </para>
    /// <para>
    /// The folder and the filename are reported apart, because they are shown apart and
    /// organizing can change either without the other. <c>DisplayPath</c> is the folder
    /// alone — the filename is already <c>FileName</c>, and repeating it would spend a
    /// column on a value the row carries twice.
    /// </para>
    /// <para>
    /// <c>ChapterHealth</c> is the same kind of evidence about the file's chapter marks:
    /// what the container carries, judged against what a book of that length should
    /// carry. <c>ChapterReason</c> says why in a sentence, so the table can show a verdict
    /// without the reader having to open the file.
    /// </para>
    /// </remarks>
    public sealed record LibraryTagRow(
        int AudiobookId,
        int FileId,
        string BookTitle,
        string FileName,
        string? RelativePath,
        string Extension,
        bool Writable,
        IReadOnlyDictionary<string, string> Tags,
        IReadOnlyDictionary<string, string> Expected,
        IReadOnlyList<string> Mismatched,
        string? Error,
        IReadOnlyList<string> LockedTags,
        string? DisplayPath = null,
        string? ExpectedPath = null,
        bool PathMismatched = false,
        bool PathLocked = false,
        string? ExpectedFileName = null,
        bool FileNameMismatched = false,
        ChapterHealth ChapterHealth = ChapterHealth.Unknown,
        string? ChapterReason = null,
        int ChapterCount = 0,
        bool ChapterRepairable = false,
        AudioAuditVerdict AudioAudit = AudioAuditVerdict.NotAudited,
        string? AudioAuditReason = null);

    /// <summary>
    /// The whole library's tag table, plus what it cost to build.
    /// </summary>
    /// <remarks>
    /// <c>FilesRead</c> counts the files this call actually probed, as opposed to the ones
    /// answered from the cache. It is reported because a cold first load takes seconds
    /// and a warm one is instant, and an operator wondering which they just got deserves
    /// an answer.
    /// </remarks>
    public sealed record LibraryTagIndex(
        IReadOnlyList<LibraryTagRow> Rows,
        int FilesRead,
        DateTime GeneratedAtUtc);

    /// <summary>
    /// Builds the library-wide tag table: every audio file, its embedded tags, and the
    /// tags Listenarr would write into it.
    /// </summary>
    public interface ILibraryTagIndexService
    {
        /// <param name="refresh">
        /// Re-probe every file rather than trusting the cache. The cache already notices a
        /// file whose size or modification time changed, so this is for the case where
        /// something outside Listenarr rewrote tags in place without disturbing either.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="audiobookIds">
        /// Only these books, when given. The table refreshes one book's rows in place
        /// after a write, and rebuilding the whole library for that would probe nothing
        /// yet still plan every row.
        /// </param>
        Task<LibraryTagIndex> BuildAsync(
            bool refresh = false,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<int>? audiobookIds = null);

        /// <summary>
        /// Probe one file now and cache the result, for the moment a write has just
        /// published it: the next table load then answers for it from the cache
        /// instead of noticing the changed file and probing it during the request.
        /// </summary>
        /// <param name="fullPath">The file as published.</param>
        /// <param name="fileId">The file's row, so its chapter verdict can be recorded beside the cache.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task RecordFileAsync(string fullPath, int? fileId = null, CancellationToken cancellationToken = default);
    }
}
