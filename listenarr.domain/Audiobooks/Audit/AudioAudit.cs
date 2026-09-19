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
namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>Whether what the narrator says the book is agrees with what the record says it is.</summary>
    public enum AudioAuditVerdict
    {
        NotAudited,

        /// <summary>The title and the author were heard.</summary>
        Match,

        /// <summary>The book is right but the narrator credited is not the one heard.</summary>
        NarratorMismatch,

        /// <summary>Neither the title nor the author was heard: this is probably a different book.</summary>
        Mismatch,

        /// <summary>Too little was heard to say: music, silence, or credits that name nothing.</summary>
        Inconclusive
    }

    /// <summary>How the stored transcript separates what was heard at the end from what was heard at the start.</summary>
    public static class AudioAuditTranscript
    {
        public const string ClosingMarker = "\n\n[closing]\n";
    }

    /// <summary>The verdict as the API spells it, or null for a book not yet audited.</summary>
    public static class AudioAuditVerdictNames
    {
        public static string? Of(AudioAuditVerdict verdict) => verdict switch
        {
            AudioAuditVerdict.Match => "match",
            AudioAuditVerdict.NarratorMismatch => "narrator-mismatch",
            AudioAuditVerdict.Mismatch => "mismatch",
            AudioAuditVerdict.Inconclusive => "inconclusive",
            _ => null
        };
    }

    /// <summary>
    /// The credits as spoken: "A War of Gifts, by Orson Scott Card, read by Scott Brick."
    /// Any part may be missing.
    /// </summary>
    public sealed record AudioCredits(string? Title, string? Author, string? Narrator)
    {
        public static AudioCredits Empty { get; } = new(null, null, null);

        public bool IsEmpty => Title == null && Author == null && Narrator == null;
    }

    /// <summary>The audit's answer and the evidence behind it.</summary>
    /// <param name="Verdict">The verdict.</param>
    /// <param name="Reason">One sentence for the badge's tooltip.</param>
    /// <param name="TitleScore">How much of the record's title was heard, 0–1.</param>
    /// <param name="AuthorScore">How much of the best-matching author's name was heard, 0–1.</param>
    /// <param name="NarratorScore">How much of the best-matching narrator's name was heard, 0–1; null when the record names no narrator.</param>
    /// <param name="Credits">What the credits said, as parsed.</param>
    public sealed record AudioAuditResult(
        AudioAuditVerdict Verdict,
        string Reason,
        double TitleScore,
        double AuthorScore,
        double? NarratorScore,
        AudioCredits Credits);
}
