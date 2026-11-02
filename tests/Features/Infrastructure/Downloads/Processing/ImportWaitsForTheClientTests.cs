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

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Processing
{
    /// <summary>
    /// "No importable files" is the right answer only once the client has finished with a
    /// download. While it is still unpacking, the only path it offers is its own working
    /// directory, and an empty look there says nothing.
    ///
    /// Measured on this library: three imports failed from /downloads/intermediate - the
    /// NZBGet InterDir - spending all three attempts within minutes while NZBGet was still
    /// working. 25 imports in all ended that way against 2,235 that succeeded.
    /// </summary>
    [Trait("Name", "ImportWaitsForTheClientTests")]
    [Trait("Category", "Downloads")]
    public sealed class ImportWaitsForTheClientTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

        private static DownloadProcessingJob JobCreated(TimeSpan ago) => new()
        {
            Id = "job",
            DownloadId = "download",
            CreatedAt = Now - ago
        };

        private static QueueItem Item(string? contentPath) => new()
        {
            Id = "client-1",
            Title = "a book",
            ContentPath = contentPath,
            SourceFiles = []
        };

        /// <summary>
        /// No final location means the client is still working, so this is "not yet".
        /// </summary>
        [Fact]
        public void AClientWithNoFinalLocationIsStillWorking()
        {
            Assert.True(DownloadProcessingJobProcessor.SourceIsNotReadyYet(
                Item(null), JobCreated(TimeSpan.FromMinutes(3)), Now));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void AnEmptyFinalLocationCountsAsNoneAtAll(string contentPath)
        {
            Assert.True(DownloadProcessingJobProcessor.SourceIsNotReadyYet(
                Item(contentPath), JobCreated(TimeSpan.FromMinutes(3)), Now));
        }

        /// <summary>
        /// Once the client names a finished location, an empty source is the real answer
        /// and the import should say so rather than waiting on nothing.
        /// </summary>
        [Fact]
        public void AClientThatNamesAFinishedLocationIsNotWaitedFor()
        {
            Assert.False(DownloadProcessingJobProcessor.SourceIsNotReadyYet(
                Item("/downloads/completed/Audiobooks/A Book"),
                JobCreated(TimeSpan.FromMinutes(3)),
                Now));
        }

        /// <summary>
        /// The wait is bounded: a client that never reports a final location has to fail
        /// honestly rather than hold a book in "importing" for ever.
        /// </summary>
        [Fact]
        public void TheWaitDoesNotGoOnForEver()
        {
            Assert.False(DownloadProcessingJobProcessor.SourceIsNotReadyYet(
                Item(null),
                JobCreated(DownloadProcessingJobProcessor.ClientFinishingWindow + TimeSpan.FromMinutes(1)),
                Now));
        }

        /// <summary>Right up to the edge of the window it is still worth waiting.</summary>
        [Fact]
        public void JustInsideTheWindowIsStillWorthWaiting()
        {
            Assert.True(DownloadProcessingJobProcessor.SourceIsNotReadyYet(
                Item(null),
                JobCreated(DownloadProcessingJobProcessor.ClientFinishingWindow - TimeSpan.FromMinutes(1)),
                Now));
        }

        /// <summary>
        /// Waiting must not spend one of the three attempts - spending them is what blocked
        /// these imports permanently while the client was still unpacking.
        /// </summary>
        [Fact]
        public void WaitingDoesNotSpendAnAttempt()
        {
            var job = JobCreated(TimeSpan.FromMinutes(1));
            job.RetryCount = 1;

            job.WaitForClient("still unpacking", TimeSpan.FromMinutes(2));

            Assert.Equal(1, job.RetryCount);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.NotNull(job.NextRetryAt);
            Assert.Contains("still unpacking", string.Join(" ", job.ProcessingLog ?? []));
        }

        /// <summary>A retry still spends one, which is the difference that matters.</summary>
        [Fact]
        public void ARealRetryStillSpendsAnAttempt()
        {
            var job = JobCreated(TimeSpan.FromMinutes(1));

            job.ScheduleRetry("no importable files found");

            Assert.Equal(1, job.RetryCount);
        }
    }
}
