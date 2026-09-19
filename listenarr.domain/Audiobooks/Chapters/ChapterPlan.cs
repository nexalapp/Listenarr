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

namespace Listenarr.Domain.Audiobooks.Chapters
{
    /// <summary>Where a repaired chapter list came from, in the order the planner prefers them.</summary>
    public enum ChapterSource
    {
        /// <summary>
        /// The list ffprobe played back. When the Nero atom is broken ffmpeg reads the
        /// QuickTime chapter track instead, and that track is untouched by the bug, so
        /// this is the whole original list under a different atom.
        /// </summary>
        Played,

        /// <summary>Audnexus's list for the book's ASIN, accepted only when the runtime matches the file.</summary>
        Audnexus,

        /// <summary>
        /// Entries recovered from inside the shifted atom. Partial by construction: the
        /// header and the first entries are gone, so an opening chapter is synthesised.
        /// </summary>
        RecoveredAtom,

        /// <summary>
        /// The marks at which the narrator was heard to announce a chapter. For a CD rip
        /// whose tracks outnumber its chapters, and for placeholder titles.
        /// </summary>
        Announcements,

        /// <summary>
        /// The shape of a short work: credits, the story in parts, credits. Nothing is
        /// announced, so the marks are kept and named for what they are.
        /// </summary>
        Credits
    }

    /// <summary>
    /// The chapter list a rewrite will produce and where it came from. Serialised onto
    /// the job so what was previewed is exactly what gets written.
    /// </summary>
    /// <param name="Source">Where the list came from.</param>
    /// <param name="Chapters">The list to write.</param>
    /// <param name="Partial">Whether something is known to be missing or guessed.</param>
    /// <param name="Note">One sentence for the preview.</param>
    /// <param name="Heard">What was heard at each chapter's mark, aligned with <paramref name="Chapters"/>, when the source is the transcript.</param>
    public sealed record ChapterPlan(
        ChapterSource Source,
        IReadOnlyList<EmbeddedChapter> Chapters,
        bool Partial,
        string Note,
        IReadOnlyList<string?>? Heard = null);

    /// <summary>A file the planner could not produce a list for, and why.</summary>
    public sealed record ChapterPlanRejection(string Reason);

    /// <summary>
    /// What a stored plan was computed against. Equal keys mean the stored answer still
    /// holds; anything else means plan again.
    /// </summary>
    public static class ChapterPlanKeys
    {
        /// <summary>
        /// Bump when the planner or the parsers learn something that would change a stored
        /// plan. Every plan is keyed on it, so a release that plans better re-plans the
        /// library on its own instead of standing behind the answers of the release before.
        /// </summary>
        public const int PlannerVersion = 2;

        /// <param name="length">The file's length in bytes.</param>
        /// <param name="lastWriteUtc">The file's last write.</param>
        /// <param name="asin">The book's ASIN, which chooses the edition.</param>
        /// <param name="transcriptionModel">The whisper model that would listen, or null when transcription is off. A bigger model hears different names.</param>
        public static string For(long length, DateTime lastWriteUtc, string? asin, string? transcriptionModel) =>
            $"{length}|{lastWriteUtc.Ticks}|{asin?.Trim().ToUpperInvariant()}|{transcriptionModel?.Trim().ToLowerInvariant()}|v{PlannerVersion}";

        /// <summary>The model a plan would listen with under these settings: none when transcription is off.</summary>
        public static string? ModelFor(bool transcriptionEnabled, string? transcriptionModel) =>
            transcriptionEnabled ? (string.IsNullOrWhiteSpace(transcriptionModel) ? "base.en" : transcriptionModel) : null;
    }

    /// <summary>A stored planning outcome: the plan, or why there is none.</summary>
    public sealed record ChapterPlanOutcome(ChapterPlan? Plan, string? Rejection);
}
