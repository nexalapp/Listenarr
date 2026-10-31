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

namespace Listenarr.Tests.Features.Infrastructure.FileSystem
{
    /// <summary>
    /// A prepared move commits its registration before it retires its source. Whether a
    /// failure after that point is reported as a failed move decides what the operator is
    /// told: on 2026-10-30 four manual imports placed and registered their files and then
    /// could not delete their sources, and every one of them was reported as "0 of 1
    /// succeeded" while the library already held the file, correctly named.
    /// </summary>
    [Trait("Name", "FileMoverPreparedMoveClassificationTests")]
    [Trait("Category", "FileSystem")]
    public sealed class FileMoverPreparedMoveClassificationTests : BaseTests
    {
        /// <summary>Everything from the registration commit onwards is the library's file.</summary>
        [Theory]
        [InlineData(FileMutationJournalState.RegistrationCommitted)]
        [InlineData(FileMutationJournalState.SourceDeletionAuthorized)]
        [InlineData(FileMutationJournalState.SourceDeleted)]
        [InlineData(FileMutationJournalState.Completed)]
        [InlineData(FileMutationJournalState.OwnerMetadataReconciled)]
        public void AStateAtOrPastRegistrationMeansTheLibraryOwnsTheFile(FileMutationJournalState state)
        {
            Assert.True(FileMover.PublicationIsCommitted(state));
        }

        /// <summary>
        /// Before the commit the move may not have happened at all, so it must never be
        /// reported as one that did.
        /// </summary>
        [Theory]
        [InlineData(FileMutationJournalState.Planned)]
        [InlineData(FileMutationJournalState.TargetIdentityPersisted)]
        [InlineData(FileMutationJournalState.TargetVerified)]
        public void AStateBeforeRegistrationIsNotASuccess(FileMutationJournalState state)
        {
            Assert.False(FileMover.PublicationIsCommitted(state));
        }

        /// <summary>
        /// A parked row is one nobody has judged yet, and it may have been parked before
        /// the registration committed - so it is not evidence of a published file.
        /// </summary>
        [Fact]
        public void AParkedRowIsNotEvidenceOfAPublishedFile()
        {
            Assert.False(FileMover.PublicationIsCommitted(FileMutationJournalState.NeedsAttention));
        }

        /// <summary>A journal that cannot be read answers no, never yes.</summary>
        [Fact]
        public void AnUnreadableJournalIsNotASuccess()
        {
            Assert.False(FileMover.PublicationIsCommitted(null));
        }
    }
}
