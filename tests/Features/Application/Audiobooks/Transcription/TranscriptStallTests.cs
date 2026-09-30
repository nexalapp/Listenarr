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
using Listenarr.Application.Audiobooks.Transcription;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Transcription
{
    /// <summary>
    /// The timings here are measured, not invented: whisper medium.en over the exact windows
    /// the audit asks for, on the file in the library. They are the evidence that a window
    /// can come back short with nothing to say so, and they are cheap to keep - a handful of
    /// spans rather than audio in the repository.
    /// </summary>
    [Trait("Name", "TranscriptStallTests")]
    [Trait("Category", "Transcription")]
    public sealed class TranscriptStallTests : BaseTests
    {
        private static HeardSpan Span(double start, double end, string text) =>
            new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

        /// <summary>
        /// The Barsoom Project, closing ninety seconds. Its production credits sit behind
        /// twelve seconds of music; whisper gave up at 37.1s, said "to life." across the next
        /// sixteen, and went quiet until the last three. Listening again from 37.1s reads
        /// every line of them, including "Copyright 1989 by Larry Niven, Stephen Barnes" -
        /// the only place in the whole recording either author is named.
        /// </summary>
        [Fact]
        [Trait("Method", "FirstStall")]
        [Trait("Scenario", "CreditsBehindMusicAtTheEnd")]
        public void FirstStall_FindsWhereTheClosingGaveUp()
        {
            List<HeardSpan> heard =
            [
                Span(0.0, 6.2, "answer. Most people don't really want to see the strings, don't want to see what lurks"),
                Span(6.2, 13.7, "behind the mirrors. They need dreams, need magic, always have. Alex couldn't hear the"),
                Span(13.7, 20.2, "sounds of laughter, of gaiety and excitement, couldn't see the individual smiles of anticipation,"),
                Span(20.2, 25.8, "but he could see the flow, the tide of life as it streamed once more into the streets"),
                Span(25.8, 27.1, "of Dream Park."),
                Span(27.1, 34.1, "\"We are the magicians,\" Griffin reminded himself proudly. \"We bring the dream to life, and"),
                Span(34.1, 37.1, "we're the only ones who can.\""),
                Span(57.1, 73.4, "to life."),
                Span(87.1, 90.1, "Audible hopes you have enjoyed this program."),
            ];

            var stall = TranscriptStall.FirstStall(heard, TimeSpan.FromSeconds(90));

            Assert.NotNull(stall);
            Assert.Equal(37.1, stall!.Value.TotalSeconds, 1);
        }

        /// <summary>
        /// The same book's opening. Two thirty-second chunks yielded two words between them:
        /// whisper read the shop ident, then the heading, and dropped the rest of both.
        /// </summary>
        [Fact]
        [Trait("Method", "FirstStall")]
        [Trait("Scenario", "TwoChunksSwallowedAtTheStart")]
        public void FirstStall_FindsWhereTheOpeningGaveUp()
        {
            List<HeardSpan> heard =
            [
                Span(0.0, 2.0, "This is Audible."),
                Span(30.0, 32.0, "Prologue"),
                Span(60.0, 90.0, "The blood-saturated teeth gleamed as it shrieked its mindless wrath. Its breath was a cold and fetid wind."),
            ];

            var stall = TranscriptStall.FirstStall(heard, TimeSpan.FromSeconds(90));

            Assert.NotNull(stall);
            Assert.Equal(2.0, stall!.Value.TotalSeconds, 1);
        }

        [Fact]
        [Trait("Method", "FirstStall")]
        [Trait("Scenario", "AWindowReadThroughIsLeftAlone")]
        public void FirstStall_IsNothingWhenTheWindowWasHeard()
        {
            // Ordinary narration: spans butt up against each other at a normal rate. Paying
            // for a second decode here would double the cost of every audit in the library.
            List<HeardSpan> heard =
            [
                Span(0.0, 7.0, "Blackstone Audio presents A War of Gifts, written by Orson Scott Card,"),
                Span(7.0, 12.0, "read by Scott Brick, Stefan Rudnicki and a full cast."),
                Span(12.0, 20.0, "Chapter one. The boy stood at the window and watched the snow come down over the yard."),
            ];

            Assert.Null(TranscriptStall.FirstStall(heard, TimeSpan.FromSeconds(20)));
        }

        [Fact]
        [Trait("Method", "FirstStall")]
        [Trait("Scenario", "ALeadInHushIsNotAStall")]
        public void FirstStall_IgnoresSilenceBeforeTheFirstWords()
        {
            // Some recordings open on a long hush. There is nothing in it to hear, and
            // listening again from zero would just repeat the window.
            List<HeardSpan> heard =
            [
                Span(14.0, 21.0, "Recorded Books presents Protector by Larry Niven, read by Tom Weiner."),
                Span(21.0, 29.0, "Chapter one. Phssthpok had been travelling for longer than he cared to count."),
            ];

            Assert.Null(TranscriptStall.FirstStall(heard, TimeSpan.FromSeconds(30)));
        }

        [Fact]
        [Trait("Method", "FirstStall")]
        [Trait("Scenario", "SilenceAtTheEndIsNotWorthADecode")]
        public void FirstStall_IgnoresATailTooShortToBeWorthIt()
        {
            List<HeardSpan> heard = [Span(0.0, 84.0, string.Join(' ', Enumerable.Repeat("words", 200)))];

            Assert.Null(TranscriptStall.FirstStall(heard, TimeSpan.FromSeconds(90)));
        }

        [Fact]
        [Trait("Method", "FirstStall")]
        [Trait("Scenario", "NothingHeardAtAll")]
        public void FirstStall_IsNothingWhenTheWindowWasSilent()
        {
            Assert.Null(TranscriptStall.FirstStall([], TimeSpan.FromSeconds(90)));
        }
    }
}
