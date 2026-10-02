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
using System.Globalization;

namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>
    /// What the two files the audit listened to looked like when it listened, so a
    /// stored transcript can be trusted or thrown away.
    ///
    /// <para>
    /// What is recorded is the audio's own length, not the file's. A tag write rewrites the
    /// container - new size, new modification time - without touching a second of audio, and
    /// an identity built from size and time therefore threw away a perfectly good transcript
    /// every time a narrator was corrected, and lapsed the acceptance that went with it. The
    /// duration survives that, and is already measured: it is on the file's record.
    /// </para>
    /// <para>
    /// It is also the better question. Two different recordings of a book practically never
    /// run to the same second; a re-encode of the same audio does, and keeping its transcript
    /// is right. What it cannot catch is a different recording of identical length swapped in
    /// without a rescan, which is a narrower hole than the one it closes.
    /// </para>
    public static class AudioAuditFileIdentity
    {
        /// <summary>
        /// Bumped when the way a window is listened to changes, so that transcripts taken
        /// the old way are heard again rather than merely re-judged. The model alone is not
        /// enough: it says which ears listened, not how well they were made to listen.
        ///
        /// <para>
        /// 2: whisper's stalls are noticed and the rest of the window is heard again
        /// (TranscriptStall). The Barsoom Project's production credits - the only place
        /// either of its authors is named aloud - sat behind twelve seconds of music that
        /// cost the fifty seconds after it, and no amount of re-judging those words could
        /// have found them.
        /// </para>
        /// </summary>
        private const int Listening = 3;

        /// <summary>
        /// Each file's audio length in whole seconds, the parts joined by "|", the model that
        /// did the listening appended after "@" with the listening version, and the offset the
        /// credits were found at after "+".
        /// </summary>
        /// <remarks>
        /// Whole seconds rather than the measurement to hand. A rescan with a different tool
        /// can shift a duration by milliseconds, and that must not be enough to throw away a
        /// transcript - which is the whole failing this replaces.
        /// </remarks>
        /// <param name="durations">Each heard file's audio length.</param>
        /// <param name="model">The whisper model that did the listening.</param>
        /// <param name="openingSkipSeconds">
        /// How far into the first file the credits were found. Appended only when it is not
        /// zero, so a book with nothing to skip keeps the identity it already had.
        /// </param>
        public static string Of(
            IEnumerable<TimeSpan> durations,
            string? model = null,
            double openingSkipSeconds = 0) =>
            string.Join('|', durations.Select(duration =>
                ((long)Math.Round(duration.TotalSeconds)).ToString(CultureInfo.InvariantCulture) + "s"))
            + (string.IsNullOrWhiteSpace(model)
                ? string.Empty
                : "@" + model.Trim() + "~" + Listening.ToString(CultureInfo.InvariantCulture))
            + (openingSkipSeconds > 0
                ? "+" + openingSkipSeconds.ToString("F1", CultureInfo.InvariantCulture)
                : string.Empty);

        /// <summary>
        /// Whether an identity's files part was written the old way, as "length:lastWriteTicks".
        ///
        /// <para>
        /// Those cannot be compared with a duration and cannot be recovered into one: the
        /// lengths they hold are of containers that have since been rewritten. They are taken
        /// at their word instead - the files they describe are treated as the files on disk -
        /// which keeps every acceptance already given, and every transcript already taken,
        /// rather than lapsing the lot on a change of format. A book audited once from here on
        /// records a duration and leaves the bridge behind.
        /// </para>
        /// </summary>
        private static bool WrittenTheOldWay(string identity) =>
            FilesOf(identity) is { Length: > 0 } files && files.Contains(':', StringComparison.Ordinal);

        /// <summary>
        /// Just the files' part of an identity, without the model that listened or the
        /// offset it started at.
        ///
        /// <para>
        /// Those suffixes exist so a transcript is re-taken when the way we listen changes,
        /// which is right for a transcript and wrong for anything about the recording
        /// itself. A person who accepts a verdict vouches for the audio they heard, not for
        /// which whisper model transcribed it, so comparing the whole string quietly threw
        /// twelve acceptances away when the format grew a suffix.
        /// </para>
        /// </summary>
        public static string? FilesOf(string? identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
            {
                return identity;
            }

            var mark = identity.IndexOf('@');
            return mark < 0 ? identity : identity[..mark];
        }

        /// <summary>
        /// How far into the first file a recorded listening began, or zero when the
        /// identity records no skip.
        /// </summary>
        /// <remarks>
        /// Read back rather than decided again, because the skip is part of the identity.
        /// Recomputing it as zero for a book that had already been re-heard past its ident
        /// made the stored transcript fail this check on every later audit: minutes of CPU
        /// to hear words we already had, and the second pass's credits replaced by the very
        /// opening they were found to hide.
        /// </remarks>
        public static TimeSpan OpeningSkipOf(string? identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
            {
                return TimeSpan.Zero;
            }

            // A skip decided by an older listener is not binding. It was the right answer
            // for how the audio was read then; The Barsoom Project's recorded 2.7s was the
            // end of its ident and the start of its music, and keeping it would have pinned
            // every later audit to the one offset that cannot hear the credits.
            if (!identity.Contains("~" + Listening.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return TimeSpan.Zero;
            }

            // Only the skip is written with a "+"; lengths, ticks and model names have none.
            var mark = identity.LastIndexOf('+');
            if (mark < 0)
            {
                return TimeSpan.Zero;
            }

            return double.TryParse(
                identity[(mark + 1)..],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.Zero;
        }

        /// <summary>
        /// Whether two identities describe the same files, ignoring how they were heard.
        /// </summary>
        public static bool SameFiles(string? left, string? right)
        {
            var a = FilesOf(left);
            var b = FilesOf(right);
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            // An acceptance given before durations were recorded is honoured. Someone listened
            // and said the record was right, and a change in how the files are described is no
            // reason to make them do it again.
            return string.Equals(a, b, StringComparison.Ordinal)
                || WrittenTheOldWay(left!)
                || WrittenTheOldWay(right!);
        }

        /// <summary>
        /// Whether what is on disk now is what was listened to. Unknown on either side is
        /// not a match: a transcript with no identity predates this check and is re-taken
        /// once, and a file that cannot be measured is not vouched for.
        /// </summary>
        public static bool Matches(string? stored, string? current)
        {
            if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(current))
            {
                return false;
            }

            if (string.Equals(stored, current, StringComparison.Ordinal))
            {
                return true;
            }

            // A transcript taken before durations were recorded is kept, provided the same
            // model listened the same way: the words are good and re-taking them costs minutes
            // of CPU a book across the whole library. Only the files part is bridged - the
            // model and the listening version still have to agree, which is what makes a
            // better listener re-hear everything.
            return WrittenTheOldWay(stored)
                && string.Equals(HeardHowOf(stored), HeardHowOf(current), StringComparison.Ordinal);
        }

        /// <summary>Which ears listened and how: everything from the "@" on.</summary>
        private static string HeardHowOf(string identity)
        {
            var mark = identity.IndexOf('@', StringComparison.Ordinal);
            return mark < 0 ? string.Empty : identity[mark..];
        }
    }
}
