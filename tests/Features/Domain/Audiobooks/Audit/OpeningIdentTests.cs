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
            // Star Force: Endless Crusade, measured. "This is Audible." runs to 1.72s, then
            // a pause to 3.24s, then the credits. Starting at 3.24s is what recovered them.
            var start = OpeningIdent.StartsAfter([Pause(1.72, 3.24), Pause(5.16, 6.05), Pause(9.88, 17.30)]);

            Assert.Equal(TimeSpan.FromSeconds(3.24), start);
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
