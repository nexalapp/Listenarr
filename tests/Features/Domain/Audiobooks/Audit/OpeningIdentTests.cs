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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks.Audit
{
    [Trait("Name", "OpeningIdentTests")]
    [Trait("Category", "Domain")]
    public sealed class OpeningIdentTests : BaseTests
    {
        private static SilenceSpan Pause(double start, double end) =>
            new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

        [Fact]
        [Trait("Method", "StartsAfter")]
        [Trait("Scenario", "SkipsAShopIdent")]
        public void StartsAfter_BeginsWhereTheIdentEnds()
        {
            // Star Force: Endless Crusade, exactly as ffmpeg reports it at the settings the
            // app uses. Note the lead-in hush at the head of the file: taking that as the
            // first pause skips six tenths of a second, leaves the ident in the window, and
            // was why the first attempt at this changed nothing.
            var start = OpeningIdent.StartsAfter(
            [
                Pause(0, 0.595669),
                Pause(1.723379, 3.23678),
                Pause(5.160272, 6.046349),
                Pause(7.395918, 8.425986)
            ]);

            Assert.Equal(TimeSpan.FromSeconds(3.23678), start);
        }

        [Fact]
        [Trait("Method", "StartsAfter")]
        [Trait("Scenario", "LeadInHushIsNotAGap")]
        public void StartsAfter_IgnoresTheHushAtTheHeadOfTheFile()
        {
            // Nothing precedes it, so it is not the gap after anything.
            Assert.Equal(TimeSpan.Zero, OpeningIdent.StartsAfter([Pause(0, 0.6)]));
        }

        [Fact]
        [Trait("Method", "StartsAfter")]
        [Trait("Scenario", "NoIdentToSkip")]
        public void StartsAfter_BeginsAtZeroWhenTheBookStartsTalkingImmediately()
        {
            // The first pause is the narrator drawing breath well into the credits.
            Assert.Equal(TimeSpan.Zero, OpeningIdent.StartsAfter([Pause(11.0, 12.0)]));
            Assert.Equal(TimeSpan.Zero, OpeningIdent.StartsAfter([]));
            Assert.Equal(TimeSpan.Zero, OpeningIdent.StartsAfter(null));
        }

        [Fact]
        [Trait("Method", "StartsAfter")]
        [Trait("Scenario", "IgnoresABreathInsideTheIdent")]
        public void StartsAfter_IgnoresAGapTooShortToBeTheEndOfAnything()
        {
            // A fifth of a second is a breath, not the end of an ident.
            Assert.Equal(TimeSpan.Zero, OpeningIdent.StartsAfter([Pause(1.0, 1.2)]));
        }

        [Fact]
        [Trait("Method", "StartsAfter")]
        [Trait("Scenario", "RefusesToThrowAwayStory")]
        public void StartsAfter_RefusesToSkipTooFar()
        {
            // A long musical opening is not an ident, and skipping ten seconds into a book
            // that starts immediately would throw away the credits rather than find them.
            Assert.Equal(TimeSpan.Zero, OpeningIdent.StartsAfter([Pause(2.0, 11.0)]));
        }
        // Verbatim whisper output from books in the library, kept as fixtures so a change
        // to the rule has to answer for the real cases rather than for invented ones.
        [Theory]
        [Trait("Method", "LooksSwallowed")]
        [Trait("Scenario", "NinetySecondsThatSayNothing")]
        [InlineData("\"This is audible.\"\nThe field square he was standing on went into freefall.")]
        [InlineData("[Music]\nby magazine February 23, 2009. Just because you're paranoid.")]
        [InlineData("1.\nHealers Dolores never met a healer she didn't like, until the night they took her away.")]
        public void LooksSwallowed_IsAboutWhatTheOpeningSaysNotHowItBegins(string opening)
        {
            // Three disguises, one symptom. The third is Invasive Procedures, whose
            // recording actually opens "Blackstone Audio presents Invasive Procedures, a
            // novel by Orson Scott Card and Aaron Johnston" - whisper rendered that whole
            // stretch as "1.", so the first line is the damage rather than the book.
            Assert.True(OpeningIdent.LooksSwallowed(opening));
        }

        [Theory]
        [Trait("Method", "LooksSwallowed")]
        [Trait("Scenario", "AnOpeningThatNamedTheBook")]
        [InlineData("2001 A Space Odyssey by Arthur C. Clarke\nCopyright 1968. Read by Dick Hill.")]
        [InlineData("This is Audible.\nBlackstone Audio presents Inferno by Larry Niven, read by Tom Weiner.")]
        [InlineData("[MUSIC PLAYING]\nHachette Audio presents Provenance, written by Ann Leckie.")]
        public void LooksSwallowed_LeavesAnOpeningThatNamedTheBook(string opening)
        {
            // 2001 opens with its own title and must never be skipped past; the other two
            // carry a badge or music and were transcribed correctly anyway.
            Assert.False(OpeningIdent.LooksSwallowed(opening));
        }
        [Fact]
        [Trait("Method", "LooksSwallowed")]
        [Trait("Scenario", "AnOpeningThatNamesTheBook")]
        public void LooksSwallowed_LeavesAnOpeningThatNamesTheBookInItsOwnWords()
        {
            // A Meeting with Medusa, verbatim. It never says "read by" or "copyright", so a
            // rule about credit wording alone called it swallowed, listened again from eight
            // seconds in, and threw the title away. Checked on the live library, where the
            // book went from match to mismatch.
            const string opening =
                "A Meeting with Medusa.\nA Day to Remember.\nThe Queen Elizabeth was five kilometers "
                + "above the Grand Canyon, dawdling along at a comfortable 180, when Howard Falcon "
                + "spotted the camera platform closing in from the right.";

            Assert.False(OpeningIdent.LooksSwallowed(opening, "A Meeting with Medusa", ["Arthur C. Clarke"]));
            Assert.True(OpeningIdent.LooksSwallowed(opening, "Some Other Book", ["Nobody At All"]));
        }

        [Fact]
        [Trait("Method", "LooksSwallowed")]
        [Trait("Scenario", "TheAuthorAloneIsEnough")]
        public void LooksSwallowed_LeavesAnOpeningThatNamesOnlyTheAuthor()
        {
            // Either half is enough to say the stretch identifies the book.
            Assert.False(OpeningIdent.LooksSwallowed(
                "Alastair Reynolds.\nGrafenwalder's attention was torn between the Ultra captain.",
                "Some Quite Different Title",
                ["Alastair Reynolds"]));

            // Half a title is not the title: "Grafenwalder's" alone leaves the book unnamed.
            Assert.True(OpeningIdent.LooksSwallowed(
                "Grafenwalder's attention was torn between the Ultra captain and the video feed.",
                "Grafenwalder's Bestiary",
                ["Alastair Reynolds"]));
        }

        [Fact]
        [Trait("Method", "LooksSwallowed")]
        [Trait("Scenario", "StillCatchesTheRealOnes")]
        public void LooksSwallowed_StillCatchesTheThreeRealCases()
        {
            Assert.True(OpeningIdent.LooksSwallowed(
                "\"This is audible.\"\nThe field square he was standing on went into freefall.",
                "Star Force: Endless Crusade", ["Aer-ki Jyr"]));
            Assert.True(OpeningIdent.LooksSwallowed(
                "[Music]\nby magazine February 23, 2009. Just because you're paranoid.",
                "Pines", ["Blake Crouch"]));
            Assert.True(OpeningIdent.LooksSwallowed(
                "1.\nHealers Dolores never met a healer she didn't like, until the night they took her away.",
                "Invasive Procedures", ["Orson Scott Card", "Aaron Johnston"]));
        }

        [Fact]
        [Trait("Method", "CreditsProbes")]
        [Trait("Scenario", "ItWalksPastTheMusic")]
        public void CreditsProbes_WalkInFromTheIdentUntilItGivesUp()
        {
            // The Barsoom Project's ident ends at 2.705s, which is also where nine seconds
            // of music begin. Every window from there is "[music]" and nothing else. The
            // second probe, 10.705s, is the one that reads the credits.
            var probes = OpeningIdent.CreditsProbes(TimeSpan.FromSeconds(2.705)).ToList();

            Assert.Equal(2.705, probes[0].TotalSeconds, 3);
            Assert.Equal(10.705, probes[1].TotalSeconds, 3);
            Assert.All(probes, at => Assert.True(at <= OpeningIdent.GiveUpAfter));
        }

        [Fact]
        [Trait("Method", "CreditsProbes")]
        [Trait("Scenario", "ASweepReachesFiveMinutes")]
        public void CreditsProbes_SweepAMinuteAtATimeToFiveMinutes()
        {
            // Forty seconds does not clear a long musical open. Fantastic Beasts: Makers,
            // Mysteries and Magic is "(music)" at nought, two hundred and four hundred
            // seconds, so the walk has to reach minutes - and cheaply, which means skipping
            // audio between probes rather than widening the window that reads them.
            var probes = OpeningIdent.CreditsProbes(TimeSpan.FromSeconds(2.705)).ToList();

            Assert.Contains(TimeSpan.FromMinutes(1), probes);
            Assert.Contains(TimeSpan.FromMinutes(5), probes);
            Assert.Equal(TimeSpan.FromMinutes(5), probes[^1]);

            // Cheap enough to run on a whole library: a handful of twenty-second decodes.
            Assert.True(probes.Count <= 12, $"too many probes for an ordinary audit: {probes.Count}");
        }

        [Fact]
        [Trait("Method", "CreditsProbes")]
        [Trait("Scenario", "ListeningFurtherLeavesNoGaps")]
        public void CreditsProbes_LeaveNoGapsWhenAskedToListenFurther()
        {
            // The sweep skips audio, so credits can fall between two of its probes. Asked for
            // by hand, the walk steps by the window instead and covers every second of it.
            var probes = OpeningIdent.CreditsProbes(TimeSpan.FromSeconds(2.705), listenFurther: true).ToList();

            for (var i = 1; i < probes.Count; i++)
            {
                Assert.True(
                    probes[i] - probes[i - 1] <= OpeningIdent.CreditsWindow,
                    $"a gap between {probes[i - 1]} and {probes[i]} is wider than the window");
            }

            Assert.True(probes[^1] >= TimeSpan.FromMinutes(4), "it should still reach minutes in");
        }

        [Theory]
        [Trait("Method", "NothingButNoise")]
        [Trait("Scenario", "OnlyMarksMeansKeepWalking")]
        [InlineData("[Music]", true)]
        [InlineData("(eerie music)", true)]
        [InlineData("(dramatic music)", true)]
        [InlineData("[MUSIC PLAYING]", true)]
        [InlineData("", true)]
        [InlineData("   ", true)]
        [InlineData("[Music] Audible Frontiers presents The Barsoom Project.", false)]
        [InlineData("Introduction.", false)]
        [InlineData("When I put together my first short story collection", false)]
        public void NothingButNoise_SeparatesSilenceFromSpeech(string heard, bool noise)
        {
            // Fantastic Beasts really does come back as nothing but a mark, three times over.
            // Dealing in Futures comes back with words from the first second - the book has
            // started, so there is nothing further in to find.
            Assert.Equal(noise, OpeningIdent.NothingButNoise(heard));
        }

        [Fact]
        [Trait("Method", "CreditsWindow")]
        [Trait("Scenario", "ShortEnoughNotToLoseThem")]
        public void CreditsWindow_IsShorterThanAWhisperChunk()
        {
            // Measured on one offset of The Barsoom Project: seventeen seconds read the
            // announcement, thirty returned nothing at all, eighty-eight returned the
            // prologue without it. A window that runs on past the credits loses them.
            Assert.True(
                OpeningIdent.CreditsWindow < TimeSpan.FromSeconds(30),
                "a window of a whole whisper chunk or more is where the credits go missing");
        }
    }
}
