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
    }
}
