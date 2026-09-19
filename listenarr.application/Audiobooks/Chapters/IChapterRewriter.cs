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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Domain.Audiobooks.Chapters;

namespace Listenarr.Application.Audiobooks.Chapters
{
    /// <summary>
    /// One file's chapter rewrite. The output goes to a scratch path; publishing it into
    /// the library is the caller's job, exactly as for a tag write.
    /// </summary>
    /// <param name="SourcePath">The file whose chapters are wrong.</param>
    /// <param name="ScratchOutputPath">Where to write the repaired copy.</param>
    /// <param name="Plan">The chapter list to write, as previewed.</param>
    /// <param name="Existing">What the source carries, read once for the verification.</param>
    public sealed record ChapterRewriteRequest(
        string SourcePath,
        string ScratchOutputPath,
        ChapterPlan Plan,
        AudiobookFileTags Existing);

    /// <summary>
    /// Rewrites a container's chapter structures without touching its audio.
    ///
    /// <para>
    /// Chapters are the one thing TagLib# cannot write, so this is the one place a remux
    /// is justified: ffmpeg copies the audio and cover as they are and writes both the
    /// Nero atom and the QuickTime track from the plan. ffmpeg drops the freeform tags
    /// the library depends on, so they are put back through the tag writer afterwards
    /// and the result is verified the same way a tag write is.
    /// </para>
    /// </summary>
    public interface IChapterRewriter
    {
        Task<TagWriteResult> RewriteAsync(
            ChapterRewriteRequest request,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reads what is left inside a shifted Nero atom. An infrastructure concern because
    /// it walks MP4 boxes; the planner only sees the list.
    /// </summary>
    public interface IChapterAtomRecovery
    {
        IReadOnlyList<Domain.Audiobooks.Conversion.EmbeddedChapter>? TryRecover(string path);
    }
}
