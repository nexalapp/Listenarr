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

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients.Nzbget
{
    /// <summary>
    /// Turning NZBGet's status word into the reason everything downstream acts on.
    ///
    /// The statuses asserted here are the ones this library has actually produced -
    /// FAILURE/HEALTH, DELETED/COPY, FAILURE/PAR, DELETED/DUPE, WARNING/PASSWORD and
    /// FAILURE/UNPACK accounted for all 200 failed downloads on 2026-10-30 - plus the
    /// forms NZBGet documents for the same conditions under different settings.
    /// </summary>
    [Trait("Name", "NzbgetFailureMessageMapperTests")]
    [Trait("Category", "DownloadClients")]
    public sealed class NzbgetFailureMessageMapperTests : BaseTests
    {
        [Theory]
        // The same condition reaches history under three categories depending on which
        // setting caught it, and all three mean the release cannot be rebuilt.
        [InlineData("FAILURE/HEALTH", DownloadFailureReason.Health)]
        [InlineData("WARNING/HEALTH", DownloadFailureReason.Health)]
        [InlineData("DELETED/HEALTH", DownloadFailureReason.Health)]
        [InlineData("FAILURE/PAR", DownloadFailureReason.Damaged)]
        [InlineData("FAILURE/REPAIR", DownloadFailureReason.Damaged)]
        [InlineData("FAILURE/UNPACK", DownloadFailureReason.Damaged)]
        [InlineData("WARNING/DAMAGED", DownloadFailureReason.Damaged)]
        [InlineData("DELETED/BAD", DownloadFailureReason.Damaged)]
        [InlineData("WARNING/PASSWORD", DownloadFailureReason.Password)]
        [InlineData("DELETED/COPY", DownloadFailureReason.Duplicate)]
        [InlineData("DELETED/DUPE", DownloadFailureReason.Duplicate)]
        [InlineData("WARNING/SPACE", DownloadFailureReason.DiskSpace)]
        [InlineData("FAILURE/MOVE", DownloadFailureReason.PostProcessing)]
        [InlineData("FAILURE/POSTPROCESS", DownloadFailureReason.PostProcessing)]
        public void AStatusIsClassifiedByWhatItSaysAboutTheRelease(
            string rawStatus,
            DownloadFailureReason expected)
        {
            Assert.Equal(expected, NzbgetFailureMessageMapper.Classify(rawStatus));
        }

        /// <summary>
        /// A status nobody has classified must stay Unknown, which is the one reason that
        /// blocks nothing. Guessing would hide a release for good.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("FAILURE/SOMETHING-NEW")]
        public void AnUnrecognisedStatusIsNotGuessedAt(string? rawStatus)
        {
            Assert.Equal(DownloadFailureReason.Unknown, NzbgetFailureMessageMapper.Classify(rawStatus));
        }

        /// <summary>
        /// A wrong password reaches history as a warning while the unpack it belongs to
        /// reaches it as a failure, so the password test has to come first or every
        /// password failure reads as mere damage and the NZB is never suspected.
        /// </summary>
        [Fact]
        public void AWrongPasswordIsNotMistakenForDamage()
        {
            Assert.Equal(DownloadFailureReason.Password, NzbgetFailureMessageMapper.Classify("WARNING/PASSWORD"));
            Assert.Contains("password was wrong", Message("WARNING/PASSWORD"));
        }

        [Fact]
        public void TheMessageSaysWhatTheReasonMeans()
        {
            Assert.Contains("health dropped below", Message("FAILURE/HEALTH"));
            Assert.Contains("verifying or repairing", Message("FAILURE/PAR"));
            Assert.Contains("unpacking", Message("FAILURE/UNPACK"));
            Assert.Contains("damaged and could not be repaired", Message("WARNING/DAMAGED"));
            Assert.Contains("duplicate", Message("DELETED/COPY"));
            Assert.Contains("disk space", Message("WARNING/SPACE"));
            Assert.Contains("post-processing or final move", Message("FAILURE/MOVE"));
        }

        /// <summary>
        /// An unclassified status is quoted rather than paraphrased: NZBGet's own word is
        /// more use to whoever reads it than a guess at what it meant.
        /// </summary>
        [Fact]
        public void AnUnrecognisedStatusIsQuotedBack()
        {
            Assert.Contains("FAILURE/SOMETHING-NEW", Message("FAILURE/SOMETHING-NEW"));
            Assert.Equal("NZBGet reported a failed download.", Message(string.Empty));
        }

        /// <summary>
        /// A failed history entry carrying nothing but the status, which is all the
        /// message depends on.
        /// </summary>
        private static string Message(string rawStatus) =>
            NzbgetFailureMessageMapper.Map(new NzbgetHistoryEntry
            {
                CanonicalNzbId = "1",
                Title = "Book Folder",
                Category = "audiobooks",
                RawStatus = rawStatus,
                Outcome = NzbgetHistoryOutcome.Failed,
                DestDir = string.Empty,
                FinalDir = string.Empty,
                TotalSizeBytes = 0,
                DownloadedSizeBytes = 0,
                HistoryTimeUtc = null
            });
    }
}
