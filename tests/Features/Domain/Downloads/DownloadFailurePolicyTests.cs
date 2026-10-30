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
    /// What each kind of failure deserves. The three decisions have to agree, so they are
    /// asserted together: a blocked release with no new search abandons the book, and a
    /// new search with no block re-grabs what just failed.
    /// </summary>
    [Trait("Name", "DownloadFailurePolicyTests")]
    [Trait("Category", "Downloads")]
    public sealed class DownloadFailurePolicyTests : BaseTests
    {
        /// <summary>
        /// The release is what failed, so it is set aside and another is looked for. The
        /// client's record of it goes too, or NZBGet discards the next grab as its copy.
        /// </summary>
        [Theory]
        [InlineData(DownloadFailureReason.Health)]
        [InlineData(DownloadFailureReason.Damaged)]
        [InlineData(DownloadFailureReason.Password)]
        public void AFailureTheReleaseCausedBlocksItAndLooksForAnother(DownloadFailureReason reason)
        {
            var response = DownloadFailurePolicy.For(reason);

            Assert.True(response.BlockRelease);
            Assert.True(response.PurgeClientRecord);
            Assert.True(response.SearchAgain);
        }

        /// <summary>
        /// A discarded duplicate fetched nothing, so it says nothing about the release:
        /// blocking it would take away a release that may be perfectly good. What has to
        /// go is the client's record of the earlier attempt, which is what caused the
        /// discard.
        /// </summary>
        [Fact]
        public void ADiscardedDuplicateClearsTheClientRecordWithoutBlockingTheRelease()
        {
            var response = DownloadFailurePolicy.For(DownloadFailureReason.Duplicate);

            Assert.False(response.BlockRelease);
            Assert.True(response.PurgeClientRecord);
            Assert.True(response.SearchAgain);
        }

        /// <summary>
        /// Nothing about another release would get past a full disk or a move that cannot
        /// be made, so searching again only spends a grab. The client's record stays for
        /// whoever has to fix it.
        /// </summary>
        [Theory]
        [InlineData(DownloadFailureReason.DiskSpace)]
        [InlineData(DownloadFailureReason.PostProcessing)]
        public void ALocalFailureIsLeftForAPerson(DownloadFailureReason reason)
        {
            var response = DownloadFailurePolicy.For(reason);

            Assert.False(response.BlockRelease);
            Assert.False(response.PurgeClientRecord);
            Assert.False(response.SearchAgain);
        }

        /// <summary>
        /// The client lost the files it had fetched. That is not the release's doing, so
        /// fetching it again is the obvious remedy and nothing is blocked.
        /// </summary>
        [Fact]
        public void AClientThatLostItsFilesGetsAnotherSearchAndNoBlock()
        {
            var response = DownloadFailurePolicy.For(DownloadFailureReason.MissingFiles);

            Assert.False(response.BlockRelease);
            Assert.True(response.SearchAgain);
        }

        /// <summary>
        /// An unclassified failure keeps the behaviour Listenarr always had. Blocking a
        /// release on a message nobody has read would hide it for good on a guess.
        /// </summary>
        [Fact]
        public void AnUnrecognisedFailureSearchesAgainAndBlocksNothing()
        {
            var response = DownloadFailurePolicy.For(DownloadFailureReason.Unknown);

            Assert.False(response.BlockRelease);
            Assert.False(response.PurgeClientRecord);
            Assert.True(response.SearchAgain);
        }

        /// <summary>
        /// Every reason has an answer. A member appended without a case would fall to the
        /// default and quietly get the mildest treatment, which for a release-caused
        /// failure is the loop this policy exists to end.
        /// </summary>
        [Fact]
        public void EveryReasonIsAnswered()
        {
            foreach (var reason in Enum.GetValues<DownloadFailureReason>())
            {
                var response = DownloadFailurePolicy.For(reason);

                // A block with no search is the one combination that abandons a book.
                Assert.False(response.BlockRelease && !response.SearchAgain);
            }
        }
    }
}
