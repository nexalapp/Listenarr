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
    /// <summary>
    /// Whether someone has listened for themselves and said the record is right, in
    /// spite of the verdict.
    ///
    /// <para>
    /// The audit is evidence, not proof. A production that names nobody, an opening
    /// buried under music, a reader whose name the transcriber cannot spell: all read as
    /// a mismatch, and without a way to say otherwise the same book is offered for
    /// repair for ever.
    /// </para>
    /// <para>
    /// Acceptance is pinned to the files it was given for. A person vouches for the
    /// recording they listened to, not for whatever occupies that path later, so a file
    /// swapped in afterwards is judged on its own and the flag returns.
    /// </para>
    /// </summary>
    public static class AudioAuditAcceptance
    {
        /// <summary>
        /// Whether the book's flag is currently overruled: someone accepted it, and the
        /// files are still the ones they accepted.
        /// </summary>
        public static bool IsAccepted(Audiobook audiobook) =>
            audiobook is not null
            && AudioAuditFileIdentity.Matches(
                audiobook.AudioAuditAcceptedIdentity,
                audiobook.AudioAuditFileIdentity);

        /// <summary>
        /// Whether the book should be offered for repair: the audio disagrees with the
        /// record and nobody has overruled it.
        /// </summary>
        public static bool NeedsAttention(Audiobook audiobook) =>
            audiobook is not null
            && audiobook.AudioAuditVerdict is AudioAuditVerdict.Mismatch or AudioAuditVerdict.NarratorMismatch
            && !IsAccepted(audiobook);
    }
}
