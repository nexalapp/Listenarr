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
    /// A timestamp on its own is not enough in either direction. A recording swapped in
    /// with its modification time preserved - <c>cp -p</c>, <c>rsync -a</c>, a restore
    /// from backup - would keep the old book's transcript and be judged as that book.
    /// Size catches those, because two different recordings are practically never the
    /// same number of bytes.
    /// </para>
    /// <para>
    /// Both are recorded rather than a content hash: hashing a 700MB file means reading
    /// all of it, and a tag write rewrites the container without touching a second of
    /// audio, so a hash of the bytes would throw away a good transcript every time a
    /// narrator was corrected. Size and last-write together are cheap, and wrong only
    /// for a replacement that matches on both.
    /// </para>
    /// </summary>
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
        private const int Listening = 2;

        /// <summary>
        /// One file as "length:lastWriteTicks", the parts joined by "|", and the model
        /// that did the listening appended after "@".
        /// </summary>
        /// <remarks>
        /// The model belongs here because a stored transcript is only as good as the ears
        /// that took it, and they can be upgraded. Without it, moving from base to medium
        /// changed nothing for any book already audited: the files had not moved, so every
        /// re-run re-judged the old words and the better model was never asked. Measured on
        /// this library, that was the difference between "This is a book called The New
        /// World" nine times and the book's actual credits.
        /// </remarks>
        /// <param name="files">Each file's length and last-write time.</param>
        /// <param name="model">The whisper model that did the listening.</param>
        /// <param name="openingSkipSeconds">
        /// How far into the first file the opening window began. Appended only when it is
        /// not zero, so a book with no ident to skip keeps the identity it already had and
        /// its stored transcript stays good.
        /// </param>
        public static string Of(
            IEnumerable<(long Length, DateTime LastWriteUtc)> files,
            string? model = null,
            double openingSkipSeconds = 0) =>
            string.Join('|', files.Select(f =>
                f.Length.ToString(CultureInfo.InvariantCulture)
                + ":"
                + f.LastWriteUtc.Ticks.ToString(CultureInfo.InvariantCulture)))
            + (string.IsNullOrWhiteSpace(model)
                ? string.Empty
                : "@" + model.Trim() + "~" + Listening.ToString(CultureInfo.InvariantCulture))
            + (openingSkipSeconds > 0
                ? "+" + openingSkipSeconds.ToString("F1", CultureInfo.InvariantCulture)
                : string.Empty);

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
            return !string.IsNullOrWhiteSpace(a)
                && !string.IsNullOrWhiteSpace(b)
                && string.Equals(a, b, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether what is on disk now is what was listened to. Unknown on either side is
        /// not a match: a transcript with no identity predates this check and is re-taken
        /// once, and a file that cannot be measured is not vouched for.
        /// </summary>
        public static bool Matches(string? stored, string? current) =>
            !string.IsNullOrWhiteSpace(stored)
            && !string.IsNullOrWhiteSpace(current)
            && string.Equals(stored, current, StringComparison.Ordinal);
    }
}
