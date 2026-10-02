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
using Listenarr.Domain.Audiobooks.Chapters;

using System.Text.RegularExpressions;

namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>
    /// Where to start listening, given that a shop's ident comes first.
    ///
    /// <para>
    /// Whisper transcribes "This is Audible.", puts it in quotation marks, and then treats
    /// the rest of that thirty-second chunk as a continuation of the same quoted utterance
    /// and emits nothing at all. The credits live in those swallowed seconds, so the book
    /// is judged on prose alone and reported as some other book.
    /// </para>
    /// <para>
    /// Measured on Star Force: Endless Crusade with the same model and settings, only the
    /// window moved. From zero, ninety seconds of audio yielded "This is audible." and then
    /// silence until the thirtieth second. From two and a half seconds it yielded the title,
    /// the author, the narrator, a chapter heading and the whole passage - strictly more,
    /// not merely different. Shortening the window does not help; a twelve-second window
    /// from zero fails exactly as badly. Only skipping the ident works.
    /// </para>
    /// <para>
    /// So: begin at the end of the first pause, when there is one early enough and long
    /// enough to be the gap after an ident. Everything else starts at zero.
    /// </para>
    /// </summary>
    public static partial class OpeningIdent
    {
        /// <summary>
        /// A shop or publisher badge read before the book proper: "This is Audible.",
        /// "Audible presents", "Recorded Books presents". Deliberately a short list of
        /// shapes rather than a guess, because skipping the first utterance of a book that
        /// opens with its own title throws the title away — measured on 2001: A Space
        /// Odyssey, whose opening line *is* "2001 A Space Odyssey by Arthur C. Clarke".
        /// </summary>
        [GeneratedRegex(@"^[""'\s]*(this is (audible|audible studios)\b|an audible original\b|[\w'&.,\- ]{0,40}\bpresents?\b)",
            RegexOptions.IgnoreCase)]
        private static partial Regex ShopIdent();

        /// <summary>Whether a line is nothing but a shop badge.</summary>
        public static bool IsShopIdent(string? line) =>
            !string.IsNullOrWhiteSpace(line)
            && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8
            && ShopIdent().IsMatch(line.Trim());

        /// <summary>
        /// Whether a line is only whisper's mark for something that is not speech -
        /// "[Music]", "(dramatic music)", "[BLANK_AUDIO]". In front of the credits it
        /// swallows them exactly as a spoken badge does.
        /// </summary>
        public static bool IsNonSpeech(string? line) =>
            !string.IsNullOrWhiteSpace(line) && SoundOnly().IsMatch(line.Trim());

        [GeneratedRegex(@"^[\[\(][^\]\)]*[\]\)]$")]
        private static partial Regex SoundOnly();

        private static readonly string[] CreditWording =
        [
            "narrated by", "read by", "performed by", "written by", "unabridged",
            "copyright", "production of", "an audiobook"
        ];

        /// <summary>
        /// Whether an opening looks like one whisper swallowed: it begins with a shop badge
        /// and then says nothing about what the book is. That is the shape the skip exists
        /// for, and the only shape it should be applied to.
        /// </summary>
        /// <param name="opening">The words heard at the start of the book.</param>
        /// <param name="title">The record's title.</param>
        /// <param name="authors">The record's authors.</param>
        public static bool LooksSwallowed(string? opening, string? title = null, IReadOnlyList<string>? authors = null)
        {
            if (string.IsNullOrWhiteSpace(opening))
            {
                return false;
            }

            // An opening that names the book is not swallowed, whatever words it used to do
            // it. A Meeting with Medusa opens "A Meeting with Medusa. A Day to Remember."
            // and never says "read by" or "copyright", so a rule about credit wording alone
            // called it swallowed, listened again from eight seconds in, and threw the title
            // away. What matters is whether the stretch identifies the book at all.
            if (!string.IsNullOrWhiteSpace(title) && AudioIdentityMatcher.SameTitle(title, opening))
            {
                return false;
            }

            foreach (var author in authors ?? [])
            {
                if (!string.IsNullOrWhiteSpace(author) && AudioIdentityMatcher.SameTitle(author, opening))
                {
                    return false;
                }
            }

            // The symptom, not the disguise. Whatever sits in front of the credits - a
            // shop's badge, a bed of music, or nothing anyone can point to - what comes back
            // is ninety seconds that say nothing about this book.
            return !CreditWording.Any(word => opening.Contains(word, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A pause beginning later than this is the narrator breathing, not the end of an ident.</summary>
        public static readonly TimeSpan LatestIdentEnds = TimeSpan.FromSeconds(6);

        /// <summary>Shorter than this is a breath within the ident rather than the gap after it.</summary>
        public static readonly TimeSpan ShortestGap = TimeSpan.FromSeconds(0.4);

        /// <summary>
        /// A pause beginning before this is the lead-in hush at the head of the file, not
        /// the gap after anything. Files commonly open with half a second of it, and taking
        /// that as the end of the ident skips nothing and leaves the ident in the window.
        /// </summary>
        public static readonly TimeSpan LeadIn = TimeSpan.FromSeconds(0.25);

        /// <summary>Never skip more than this, whatever the pauses say. Beyond it, story is being thrown away.</summary>
        public static readonly TimeSpan MostToSkip = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Where to start when the opening was swallowed and there is no pause to start
        /// after, because the thing in front of the credits is music rather than speech.
        ///
        /// <para>
        /// Music has no pauses, so silence detection finds nothing and the pause rule has
        /// no answer. Pines opens on a bed of it and whisper returns "[Music]" and then
        /// skips to the thirtieth second; from eight seconds it returns "Brilliance Audio
        /// presents the unabridged recording of Pines by Blake Crouch, performed by Paul
        /// Michael Garcia". Eight seconds is short enough that a book announcing itself
        /// immediately has already been caught by the first pass.
        /// </para>
        /// </summary>
        public static readonly TimeSpan PastAnIntro = TimeSpan.FromSeconds(8);

        /// <summary>
        /// A window for hearing credits and nothing else. Short on purpose.
        ///
        /// <para>
        /// Measured on The Barsoom Project, one offset, three lengths: from 2.7s a
        /// seventeen-second window read the announcement, a thirty-second window returned
        /// nothing whatsoever, and eighty-eight seconds returned the prologue with the
        /// announcement missing. A window that runs on past the credits gives whisper room
        /// to lose them.
        /// </para>
        /// </summary>
        public static readonly TimeSpan CreditsWindow = TimeSpan.FromSeconds(20);

        /// <summary>How much further in to try when a window came back without credits.</summary>
        public static readonly TimeSpan ProbeStep = TimeSpan.FromSeconds(8);

        /// <summary>
        /// How far in the dense part of the walk goes. Close in, a step has to be smaller
        /// than the window or a credit read across the seam is cut in half.
        /// </summary>
        public static readonly TimeSpan CloseIn = TimeSpan.FromSeconds(40);

        /// <summary>
        /// Past the close-in steps, a sweep: one probe a minute out to five. Measured on
        /// Fantastic Beasts: The Crimes of Grindelwald, whose every twenty-second window is
        /// "(music)" at nought, two hundred and four hundred seconds. A long musical open
        /// is not rare and forty seconds does not clear one.
        /// </summary>
        public static readonly TimeSpan SweepStep = TimeSpan.FromMinutes(1);

        /// <summary>How far in to keep looking at all.</summary>
        public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How late a chapter mark may be and still be the end of the book's front matter.
        ///
        /// <para>
        /// Deliberately further out than the sweep gives up. Stepping over a foreword is one
        /// decode at a known offset, not a walk, so it can afford to reach past where blind
        /// probing stops: Dealing in Futures' introduction ends at 331.7s, which is beyond
        /// five minutes, and that is the whole case this exists for.
        /// </para>
        /// </summary>
        public static readonly TimeSpan FrontMatterEndsBy = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Where to try listening for the credits, in order, given where the ident ends.
        ///
        /// <para>
        /// One offset is not enough, and the first one is wrong more often than not. The
        /// pause rule stops at the end of the ident, which on The Barsoom Project is 2.7s -
        /// and 2.7s is where nine seconds of music begin. Every window starting there comes
        /// back as "[music]" and nothing else, at any length. From 10.7s, once the music is
        /// behind it, whisper reads "Audible Frontiers presents The Barsoom Project. Written
        /// by Larry Niven and Stephen Barnes and narrated by Stefan Rudnicki" - the title,
        /// both authors and the narrator, none of which is said anywhere else in the
        /// recording.
        /// </para>
        /// <para>
        /// So walk. There is nothing to measure that distinguishes a music bed from a pause
        /// - silence detection cannot see music, being loud - and the cheap way to find the
        /// far side of one is to listen past it in short steps and stop at the first window
        /// that reads like credits.
        /// </para>
        /// </summary>
        /// <param name="identEnd">Where the shop's ident stops, or zero when none was found.</param>
        /// <param name="listenFurther">
        /// Leave no gaps: step by the window rather than by a minute, all the way out. Asked
        /// for by hand, for a book known to keep its credits behind a long open, because a
        /// sweep of one probe a minute can pass straight over them.
        /// </param>
        public static IEnumerable<TimeSpan> CreditsProbes(TimeSpan identEnd, bool listenFurther = false)
        {
            var from = identEnd > TimeSpan.Zero ? identEnd : PastAnIntro;

            if (listenFurther)
            {
                for (var at = from; at <= GiveUpAfter; at += CreditsWindow)
                {
                    yield return at;
                }

                yield break;
            }

            // Close in, where a credit read across a seam would be cut in half.
            for (var at = from; at <= CloseIn; at += ProbeStep)
            {
                yield return at;
            }

            // Then a sweep. These skip audio, which is the trade for reaching five minutes
            // in five probes; a book whose credits fall in one of the gaps needs the walk
            // asked for by hand.
            for (var at = SweepStep; at <= GiveUpAfter; at += SweepStep)
            {
                if (at > CloseIn)
                {
                    yield return at;
                }
            }
        }

        /// <summary>
        /// Whether a window came back with no words in it at all - only whisper's marks for
        /// music or other noise, "[Music]", "(eerie music)".
        ///
        /// <para>
        /// This is what says to keep walking. Nothing was said there, so there is nothing to
        /// have missed, and the thing in front of the credits has not finished. Real speech
        /// that is not a credit means the book has started and the announcement is not ahead
        /// of us, so the walk stops - which is what keeps it from costing ten decodes on
        /// every book whose opening happens not to name itself.
        /// </para>
        /// </summary>
        public static bool NothingButNoise(string? heard) =>
            string.IsNullOrWhiteSpace(SoundMarks().Replace(heard ?? string.Empty, string.Empty));

        // whisper writes music as a bracketed word, "[Music]", "(dramatic music)", and also
        // as bare notes: the stored opening of Fantastic Beasts reads "(dramatic music)" and
        // then "♪♪". A line of notes left the walk thinking it had heard speech and stop.
        [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*|[\s.,;:!?♪♫♬♩-]")]
        private static partial Regex SoundMarks();

        /// <summary>How far into the file the opening window should begin.</summary>
        public static TimeSpan StartsAfter(IReadOnlyList<SilenceSpan>? pauses)
        {
            if (pauses is not { Count: > 0 })
            {
                return TimeSpan.Zero;
            }

            // The gap wanted is the one after the ident, so it must follow some speech.
            var first = pauses
                .Where(pause => pause.Start >= LeadIn
                    && pause.Start < LatestIdentEnds
                    && pause.End - pause.Start >= ShortestGap)
                .OrderBy(pause => pause.Start)
                .FirstOrDefault();

            if (first.End <= TimeSpan.Zero || first.End > MostToSkip)
            {
                return TimeSpan.Zero;
            }

            return first.End;
        }
    }
}
