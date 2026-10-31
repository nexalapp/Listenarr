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

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Downloads
{
    /// <summary>
    /// Recognising a release that already failed when a later search turns it up again.
    /// Too loose and a block takes away releases that were never tried; too tight and the
    /// same release is grabbed a second time and the loop is back.
    /// </summary>
    [Trait("Name", "BlockedReleaseMatchTests")]
    [Trait("Category", "Downloads")]
    public sealed class BlockedReleaseMatchTests : BaseTests
    {
        private const string Title = "Ender's Game - Orson Scott Card [2013] M4B 64k";

        private static BlockedRelease Blocked(
            string title = Title,
            long? size = 500_000_000,
            string? releaseId = null) => new()
            {
                ReleaseTitle = title,
                Fingerprint = BlockedReleaseMatch.Fingerprint(title),
                SizeBytes = size,
                ReleaseId = releaseId
            };

        [Fact]
        public void TheSameReleaseAtTheSameSizeIsRecognised()
        {
            Assert.True(BlockedReleaseMatch.Matches(Blocked(), Title, null, 500_000_000));
        }

        /// <summary>
        /// Indexers list one posting with different separators and bracketing, and none of
        /// that distinguishes one posting from another.
        /// </summary>
        [Theory]
        [InlineData("Ender's Game - Orson Scott Card (2013) M4B 64k")]
        [InlineData("Enders.Game.-.Orson.Scott.Card.2013.M4B.64k")]
        [InlineData("  ENDERS GAME - ORSON SCOTT CARD [2013] M4B 64K  ")]
        public void ThePunctuationAnIndexerAddsDoesNotHideIt(string listedElsewhere)
        {
            Assert.True(BlockedReleaseMatch.Matches(Blocked(), listedElsewhere, null, 500_000_000));
        }

        /// <summary>
        /// Sizes for one posting differ a little between indexers - some count the par2
        /// volumes - and a block must survive that.
        /// </summary>
        [Fact]
        public void ASizeReportedALittleDifferentlyStillMatches()
        {
            Assert.True(BlockedReleaseMatch.Matches(Blocked(), Title, null, 530_000_000));
        }

        /// <summary>
        /// Another release of the same book is the thing the search is supposed to reach.
        /// </summary>
        [Fact]
        public void ADifferentReleaseOfTheSameBookIsNotBlocked()
        {
            Assert.False(BlockedReleaseMatch.Matches(
                Blocked(),
                "Ender's Game - Orson Scott Card [1994] MP3 128k",
                null,
                800_000_000));
        }

        /// <summary>
        /// A title that happens to match while the size is nowhere near is a different
        /// upload - an abridgement, or one part of a split - and has not been tried.
        /// </summary>
        [Fact]
        public void TheSameTitleAtAVeryDifferentSizeIsNotBlocked()
        {
            Assert.False(BlockedReleaseMatch.Matches(Blocked(), Title, null, 50_000_000));
        }

        /// <summary>
        /// The indexer's own id settles it outright: an indexer that renames its listings
        /// still points at the same posting.
        /// </summary>
        [Fact]
        public void TheIndexersOwnIdSettlesItWhateverTheTitleSays()
        {
            var entry = Blocked(releaseId: "abc-123");

            Assert.True(BlockedReleaseMatch.Matches(
                entry,
                "something else entirely",
                "ABC-123",
                999));
        }

        /// <summary>
        /// Nothing is known about a release with no title, so it must match nothing - one
        /// nameless block that matched every nameless release would empty the search.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("...")]
        public void AReleaseWithNoNameMatchesNothing(string? nothing)
        {
            Assert.False(BlockedReleaseMatch.Matches(Blocked(), nothing, null, 500_000_000));
        }

        /// <summary>
        /// A size nobody reported cannot contradict the title, and refusing to match on
        /// that account would let the release straight back through.
        /// </summary>
        [Fact]
        public void AnUnknownSizeDoesNotPreventAMatch()
        {
            Assert.True(BlockedReleaseMatch.Matches(Blocked(size: null), Title, null, 0));
            Assert.True(BlockedReleaseMatch.Matches(Blocked(), Title, null, 0));
        }

        [Fact]
        public void TheFingerprintKeepsOnlyWhatIdentifiesAPosting()
        {
            Assert.Equal("endersgame2013m4b64k", BlockedReleaseMatch.Fingerprint("Ender's Game [2013] M4B 64k"));
            Assert.Equal(string.Empty, BlockedReleaseMatch.Fingerprint(null));
        }
    }
}
