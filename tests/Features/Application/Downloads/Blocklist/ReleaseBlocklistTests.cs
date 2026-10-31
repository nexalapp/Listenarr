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

namespace Listenarr.Tests.Features.Application.Downloads.Blocklist
{
    /// <summary>
    /// The list a search consults before it picks a release, and the record it is built
    /// from when a download fails.
    /// </summary>
    [Trait("Name", "ReleaseBlocklistTests")]
    [Trait("Category", "Downloads")]
    public sealed class ReleaseBlocklistTests : BaseTests
    {
        [Fact]
        public void ABookWithNoFailuresBlocksNothing()
        {
            var blocklist = ReleaseBlocklist.Empty;

            Assert.Equal(0, blocklist.Count);
            Assert.False(blocklist.IsBlocked("Any Release", "id", 1));
        }

        [Fact]
        public void AReleaseThatFailedIsRecognisedAndNamed()
        {
            var blocklist = new ReleaseBlocklist([Entry("Dune - Frank Herbert M4B", 900)]);

            Assert.True(blocklist.IsBlocked("Dune - Frank Herbert M4B", null, 900));
            Assert.Equal("Dune - Frank Herbert M4B", blocklist.Find("Dune - Frank Herbert M4B", null, 900)?.ReleaseTitle);
        }

        [Fact]
        public void TheReleasesThatDidNotFailAreLeftAlone()
        {
            var blocklist = new ReleaseBlocklist([
                Entry("Dune - Frank Herbert M4B", 900),
                Entry("Dune - Frank Herbert MP3 CD rip", 1500)
            ]);

            Assert.False(blocklist.IsBlocked("Dune - Frank Herbert [2019] M4B 128k", null, 1200));
        }

        /// <summary>
        /// The block is built from the download row, because by the time a download fails
        /// the search results it came from are long gone.
        /// </summary>
        [Fact]
        public void ABlockIsBuiltFromTheFailedDownload()
        {
            var download = new Download
            {
                Id = "download-1",
                AudiobookId = 42,
                Title = "Dune - Frank Herbert [2019] M4B 128k",
                TotalSize = 1_234_567
            };
            download.SetMetadata(BlockedRelease.ReleaseIndexerKey, "NZBGeek");
            download.SetMetadata(BlockedRelease.ReleaseIdKey, "geek-77");

            var entry = BlockedRelease.For(download, DownloadFailureReason.Health, "health too low");

            Assert.Equal(42, entry.AudiobookId);
            Assert.Equal("Dune - Frank Herbert [2019] M4B 128k", entry.ReleaseTitle);
            Assert.Equal(BlockedReleaseMatch.Fingerprint(download.Title), entry.Fingerprint);
            Assert.Equal("NZBGeek", entry.Indexer);
            Assert.Equal("geek-77", entry.ReleaseId);
            Assert.Equal(1_234_567, entry.SizeBytes);
            Assert.Equal(DownloadFailureReason.Health, entry.Reason);
            Assert.Equal("health too low", entry.Detail);
            Assert.Equal("download-1", entry.DownloadId);
        }

        /// <summary>
        /// A download whose size was never reported must not be stored as blocked at size
        /// zero: the match reads a known size as corroboration, and zero is not a size.
        /// </summary>
        [Fact]
        public void ADownloadWithNoKnownSizeIsBlockedOnItsNameAlone()
        {
            var download = new Download { Id = "d", AudiobookId = 1, Title = "A Release", TotalSize = 0 };

            var entry = BlockedRelease.For(download, DownloadFailureReason.Damaged, null);

            Assert.Null(entry.SizeBytes);
            Assert.True(new ReleaseBlocklist([entry]).IsBlocked("A Release", null, 4_000_000));
        }

        /// <summary>
        /// Newznab lets an indexer put a link in a result's guid, and a link can carry its
        /// API key. Nothing that looks like one is kept: the database is the wrong place
        /// for a credential, and a link identifies no release that a title does not.
        /// </summary>
        [Theory]
        [InlineData("https://indexer.example/details?id=7&apikey=secret")]
        [InlineData("http://indexer.example/getnzb/abc")]
        [InlineData("abc?apikey=secret")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void AnIdThatCouldCarryACredentialIsNotKept(string? candidateId)
        {
            Assert.Null(BlockedRelease.SafeReleaseId(candidateId));
        }

        [Theory]
        [InlineData("3f2a9c1e4b", "3f2a9c1e4b")]
        [InlineData("abook:481422", "abook:481422")]
        [InlineData("  geek-77  ", "geek-77")]
        public void AnOrdinaryIndexerIdIsKept(string candidateId, string expected)
        {
            Assert.Equal(expected, BlockedRelease.SafeReleaseId(candidateId));
        }

        private static BlockedRelease Entry(string title, long size) => new()
        {
            AudiobookId = 1,
            ReleaseTitle = title,
            Fingerprint = BlockedReleaseMatch.Fingerprint(title),
            SizeBytes = size
        };
    }
}
