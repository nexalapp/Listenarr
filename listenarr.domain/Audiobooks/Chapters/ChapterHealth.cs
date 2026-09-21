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
namespace Listenarr.Domain.Audiobooks.Chapters
{
    /// <summary>
    /// What a file's chapter marks are worth, as far as it can be told without listening.
    /// </summary>
    public enum ChapterHealth
    {
        /// <summary>Not inspected: an MP3, or a row read before chapters were recorded.</summary>
        Unknown,

        Healthy,

        /// <summary>
        /// The container carries a chapter atom that does not parse, or that ffprobe reads
        /// differently from what the bytes say. The TagLib# shift documented on
        /// <c>NeroChapterAtom</c> is the known cause.
        /// </summary>
        Corrupt,

        /// <summary>
        /// Many short, evenly-sized chapters: CD tracks that were never merged into the
        /// author's chapters.
        /// </summary>
        Oversegmented,

        /// <summary>The marks may be right but every title is a placeholder.</summary>
        GenericTitles,

        /// <summary>A long book with no chapter marks at all.</summary>
        None
    }

    /// <summary>The verdict as the API spells it, one word the client can filter on.</summary>
    public static class ChapterHealthNames
    {
        public static string Of(ChapterHealth health) => health switch
        {
            ChapterHealth.Healthy => "healthy",
            ChapterHealth.Corrupt => "corrupt",
            ChapterHealth.Oversegmented => "oversegmented",
            ChapterHealth.GenericTitles => "generic-titles",
            ChapterHealth.None => "none",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Orders verdicts by how much they matter, so a book's files can be summed up by
    /// their worst one. A broken atom outranks CD tracks, which outrank a shrug.
    /// </summary>
    public static class ChapterHealthSeverity
    {
        public static int Rank(ChapterHealth health) => health switch
        {
            ChapterHealth.Corrupt => 5,
            ChapterHealth.Oversegmented => 4,
            ChapterHealth.GenericTitles => 3,
            ChapterHealth.None => 2,
            ChapterHealth.Healthy => 1,
            _ => 0
        };

        /// <summary>The verdicts a repair exists for.</summary>
        public static bool IsIssue(ChapterHealth health) =>
            health is ChapterHealth.Corrupt or ChapterHealth.Oversegmented;

        /// <summary>The verdicts a repair may have something to do for, including a retitle and a list found from nothing.</summary>
        public static bool IsRepairableKind(ChapterHealth health) =>
            health is ChapterHealth.Corrupt or ChapterHealth.Oversegmented or ChapterHealth.GenericTitles or ChapterHealth.None;

        /// <summary>
        /// Whether a repair has a source to work from, judged cheaply from what is known
        /// without planning: a broken atom is rebuilt from the chapter track that
        /// survived it or from the edition's list; tracks, placeholder titles and a file
        /// with no marks need the edition's list or a narrator to listen to. The preview
        /// gives the definitive answer; this is what the badge is coloured by before
        /// anyone asks.
        /// </summary>
        public static bool LikelyRepairable(ChapterHealth health, ChapterAtomState? atoms, bool hasAsin, bool transcriptionEnabled) =>
            health switch
            {
                ChapterHealth.Corrupt => (atoms?.HasChapterTrack ?? false) || hasAsin,
                ChapterHealth.Oversegmented or ChapterHealth.GenericTitles or ChapterHealth.None => hasAsin || transcriptionEnabled,
                _ => false
            };
    }

    /// <summary>
    /// What the container's own bytes say about chapters, read without ffprobe. Produced by
    /// the infrastructure atom inspector and judged here, so the judgement can be tested
    /// without a file.
    /// </summary>
    /// <param name="HasNeroAtom">A <c>moov/udta/chpl</c> atom exists.</param>
    /// <param name="NeroAtomError">Why the atom does not parse; null when it does or when there is none.</param>
    /// <param name="NeroChapterCount">Chapters the atom declares, when it parses.</param>
    /// <param name="HasChapterTrack">A track is referenced through <c>tref/chap</c>: the QuickTime chapter track.</param>
    public sealed record ChapterAtomState(
        bool HasNeroAtom,
        string? NeroAtomError,
        int NeroChapterCount,
        bool HasChapterTrack)
    {
        public bool NeroAtomValid => HasNeroAtom && NeroAtomError == null;
    }

    /// <summary>The verdict and the evidence it rests on.</summary>
    public sealed record ChapterHealthReport(
        ChapterHealth Health,
        string Reason,
        int ChapterCount,
        TimeSpan MedianLength)
    {
        public static ChapterHealthReport Unknown { get; } =
            new(ChapterHealth.Unknown, "Chapters have not been inspected.", 0, TimeSpan.Zero);
    }
}
