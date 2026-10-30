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

namespace Listenarr.Tests.Features.Infrastructure.Persistence
{
    /// <summary>
    /// Storing and reading the releases a search has to pass over, and naming the reason
    /// for failures recorded before the reason was kept.
    /// </summary>
    [Trait("Name", "EfReleaseBlocklistRepositoryTests")]
    [Trait("Category", "Persistence")]
    public sealed class EfReleaseBlocklistRepositoryTests : BaseTests
    {
        private IReleaseBlocklistRepository Blocklist =>
            _provider.GetRequiredService<IReleaseBlocklistRepository>();

        [Fact]
        public async Task ABlockIsReadBackForItsOwnBookOnly()
        {
            await Blocklist.AddAsync(Entry(1, "Dune M4B"));
            await Blocklist.AddAsync(Entry(2, "Dune MP3"));

            var forFirst = await Blocklist.GetForAudiobookAsync(1);

            Assert.Single(forFirst);
            Assert.Equal("Dune M4B", forFirst[0].ReleaseTitle);
        }

        /// <summary>
        /// A release can fail twice while two grabs are in flight, and the failure of the
        /// second must not add a second row saying the same thing.
        /// </summary>
        [Fact]
        public async Task BlockingTheSameReleaseTwiceLeavesOneBlock()
        {
            var first = await Blocklist.AddAsync(Entry(1, "Dune M4B"));
            var second = await Blocklist.AddAsync(Entry(1, "Dune M4B"));

            Assert.Equal(first.Id, second.Id);
            Assert.Single(await Blocklist.GetForAudiobookAsync(1));
        }

        [Fact]
        public async Task ABlockCanBeLifted()
        {
            var entry = await Blocklist.AddAsync(Entry(1, "Dune M4B"));

            Assert.True(await Blocklist.DeleteAsync(entry.Id));
            Assert.False(await Blocklist.DeleteAsync(entry.Id));
            Assert.Empty(await Blocklist.GetForAudiobookAsync(1));
        }

        [Fact]
        public async Task EveryBlockOnOneBookCanBeLiftedAtOnce()
        {
            await Blocklist.AddAsync(Entry(1, "Dune M4B"));
            await Blocklist.AddAsync(Entry(1, "Dune MP3"));
            await Blocklist.AddAsync(Entry(2, "Other Book"));

            Assert.Equal(2, await Blocklist.DeleteForAudiobookAsync(1));
            Assert.Empty(await Blocklist.GetForAudiobookAsync(1));
            Assert.Single(await Blocklist.GetForAudiobookAsync(2));
        }

        /// <summary>
        /// The backlog of failures recorded before the reason was a column keeps the
        /// client's status in its metadata, and that is enough to name the reason. Until
        /// this ran, every one of them said only "Failed".
        /// </summary>
        [Fact]
        public async Task TheReasonForAnAlreadyRecordedFailureIsNamedFromItsClientStatus()
        {
            await _downloadRepository.AddAsync(Recorded("health", DownloadStatus.Failed, "FAILURE/HEALTH"));
            await _downloadRepository.AddAsync(Recorded("dupe", DownloadStatus.Failed, "DELETED/COPY"));

            var classified = ClassifyRecordedFailures();

            Assert.Equal(2, classified);
            Assert.Equal(DownloadFailureReason.Health, (await _downloadRepository.GetByIdAsync("health"))?.FailureReason);
            Assert.Equal(DownloadFailureReason.Duplicate, (await _downloadRepository.GetByIdAsync("dupe"))?.FailureReason);
        }

        /// <summary>
        /// A status this build cannot read is left unnamed rather than written as Unknown,
        /// so a later build that understands it can still have its say. A download that did
        /// not fail is not touched at all.
        /// </summary>
        [Fact]
        public async Task AFailureNobodyCanClassifyAndADownloadThatDidNotFailAreLeftAlone()
        {
            await _downloadRepository.AddAsync(Recorded("strange", DownloadStatus.Failed, "SOMETHING/ELSE"));
            await _downloadRepository.AddAsync(Recorded("fine", DownloadStatus.Moved, "FAILURE/HEALTH"));

            Assert.Equal(0, ClassifyRecordedFailures());
            Assert.Null((await _downloadRepository.GetByIdAsync("strange"))?.FailureReason);
            Assert.Null((await _downloadRepository.GetByIdAsync("fine"))?.FailureReason);
        }

        /// <summary>Running it again finds nothing left to name.</summary>
        [Fact]
        public async Task NamingTheReasonsTwiceChangesNothingTheSecondTime()
        {
            await _downloadRepository.AddAsync(Recorded("health", DownloadStatus.Failed, "FAILURE/HEALTH"));

            Assert.Equal(1, ClassifyRecordedFailures());
            Assert.Equal(0, ClassifyRecordedFailures());
        }

        private int ClassifyRecordedFailures()
        {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
            return ListenarrDatabaseMigrationPreflight.ClassifyRecordedFailures(db);
        }

        private static Download Recorded(string id, DownloadStatus status, string clientStatus)
        {
            var download = new Download
            {
                Id = id,
                Title = id,
                Status = status,
                Metadata = []
            };
            download.SetMetadata("ClientFailureReason", clientStatus);
            return download;
        }

        private static BlockedRelease Entry(int audiobookId, string title) => new()
        {
            AudiobookId = audiobookId,
            ReleaseTitle = title,
            Fingerprint = BlockedReleaseMatch.Fingerprint(title),
            Reason = DownloadFailureReason.Health,
            SizeBytes = 1_000
        };
    }
}
