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

namespace Listenarr.Tests.Features.Application.Downloads.Submission
{
    /// <summary>
    /// A failure a book has moved on from is not news. Without retiring them, 151 rows on
    /// this library were 35 books - one release tried nine times, nine rows saying the
    /// same thing - and the only way any of them left Activity was somebody deleting it.
    /// </summary>
    [Trait("Name", "RetireSupersededFailuresTests")]
    [Trait("Category", "Downloads")]
    public sealed class RetireSupersededFailuresTests : BaseTests
    {
        private const int Book = 7788;

        private IDownloadService Downloads => _provider.GetRequiredService<IDownloadService>();

        private async Task<Download> ExistingAsync(string id, DownloadStatus status)
        {
            var download = new Download
            {
                Id = id,
                AudiobookId = Book,
                Title = id,
                Status = status,
                Metadata = []
            };
            await _downloadRepository.AddAsync(download);
            return download;
        }

        /// <summary>
        /// The rule is exercised directly. A submission cannot be driven to completion in
        /// a test host - there is no download client to send to, and the grab throws
        /// before its row is written - while the retirement runs after that row exists.
        /// </summary>
        private async Task GrabSupersedesTheOldRowsAsync() =>
            await ((DownloadService)Downloads).RetireSupersededFailuresAsync(Book, "the-new-download");

        [Fact]
        public async Task AFailedRowIsRetiredWhenSomethingElseIsTried()
        {
            await ExistingAsync("failed-before", DownloadStatus.Failed);

            await GrabSupersedesTheOldRowsAsync();

            Assert.Null(await _downloadRepository.GetByIdAsync("failed-before"));
        }

        [Fact]
        public async Task AnImportBlockedRowIsRetiredToo()
        {
            await ExistingAsync("blocked-before", DownloadStatus.ImportBlocked);

            await GrabSupersedesTheOldRowsAsync();

            Assert.Null(await _downloadRepository.GetByIdAsync("blocked-before"));
        }

        /// <summary>
        /// Only finished failures go. A download still working, or one already imported, is
        /// not something the new grab has superseded.
        /// </summary>
        [Theory]
        [InlineData(DownloadStatus.Downloading)]
        [InlineData(DownloadStatus.ImportPending)]
        [InlineData(DownloadStatus.Moved)]
        [InlineData(DownloadStatus.Paused)]
        public async Task ARowThatIsNotAFinishedFailureIsKept(DownloadStatus status)
        {
            await ExistingAsync("keep-me", status);

            await GrabSupersedesTheOldRowsAsync();

            Assert.NotNull(await _downloadRepository.GetByIdAsync("keep-me"));
        }

        /// <summary>Another book's failure is none of this grab's business.</summary>
        [Fact]
        public async Task AnotherBooksFailureIsLeftAlone()
        {
            var other = new Download
            {
                Id = "another-book",
                AudiobookId = Book + 1,
                Title = "another-book",
                Status = DownloadStatus.Failed,
                Metadata = []
            };
            await _downloadRepository.AddAsync(other);

            await GrabSupersedesTheOldRowsAsync();

            Assert.NotNull(await _downloadRepository.GetByIdAsync("another-book"));
        }
    }
}
