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

using Listenarr.Infrastructure.Downloads.Import;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Import
{
    /// <summary>
    /// An import commits in one transaction and the download is marked Moved by it. When
    /// something writes ImportPending back over that, the download sits in Activity as
    /// work that was already done. These rows have to find their own way back.
    /// </summary>
    [Trait("Name", "StalledImportRecoveryProcessorTests")]
    [Trait("Category", "Downloads")]
    public sealed class StalledImportRecoveryProcessorTests : BaseTests
    {
        private readonly Mock<IDownloadRepository> _downloads = new();
        private readonly Mock<IDownloadService> _downloadService = new();
        private readonly Mock<IDownloadProcessingJobService> _jobs = new();
        private readonly Mock<IDownloadClientGateway> _gateway = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly List<Download> _saved = [];

        private StalledImportRecoveryProcessor Build(params Download[] awaiting)
        {
            _downloads
                .Setup(r => r.GetAwaitingImportAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(awaiting.ToList());
            _downloadService
                .Setup(s => s.UpdateAsync(It.IsAny<Download>()))
                .Callback<Download>(_saved.Add)
                .Returns(Task.CompletedTask);

            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(IDownloadRepository))).Returns(_downloads.Object);
            provider.Setup(p => p.GetService(typeof(IDownloadService))).Returns(_downloadService.Object);
            provider.Setup(p => p.GetService(typeof(IDownloadProcessingJobService))).Returns(_jobs.Object);
            provider.Setup(p => p.GetService(typeof(IDownloadClientGateway))).Returns(_gateway.Object);
            provider.Setup(p => p.GetService(typeof(IConfigurationService))).Returns(_configuration.Object);

            _configuration
                .Setup(c => c.GetDownloadClientConfigurationAsync(It.IsAny<string>()))
                .ReturnsAsync(new DownloadClientConfiguration { Id = "client", IsEnabled = true });

            var scope = new Mock<IServiceScope>();
            scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
            var scopeFactory = new Mock<IServiceScopeFactory>();
            scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

            return new StalledImportRecoveryProcessor(
                NullLogger<StalledImportRecoveryProcessor>.Instance,
                scopeFactory.Object);
        }

        private static Download Awaiting(string id, DownloadStatus status, TimeSpan? age = null) => new()
        {
            Id = id,
            Title = id,
            Status = status,
            DownloadClientId = "client",
            StartedAt = DateTime.UtcNow - (age ?? TimeSpan.FromHours(2))
        };

        /// <summary>A job as the import leaves it when it commits.</summary>
        private void JobsFor(string downloadId, bool completed, bool committed)
        {
            var job = new DownloadProcessingJob { Id = $"job-{downloadId}", DownloadId = downloadId };
            if (committed)
            {
                job.SetCheckpoint("ImportCommitted");
            }

            if (completed)
            {
                job.MarkAsCompleted();
            }

            _jobs.Setup(s => s.GetJobsForDownloadAsync(downloadId)).ReturnsAsync([job]);
        }

        [Fact]
        public async Task ADownloadWhoseImportCommittedIsMarkedImported()
        {
            var download = Awaiting("pending", DownloadStatus.ImportPending);
            JobsFor("pending", completed: true, committed: true);

            await Build(download).RunCycleAsync(CancellationToken.None);

            Assert.Equal(DownloadStatus.Moved, download.Status);
            Assert.Single(_saved);
        }

        /// <summary>
        /// The other half of the same race: the retry found the source already consumed
        /// and blocked the import that had in fact succeeded.
        /// </summary>
        [Fact]
        public async Task ABlockedDownloadWhoseImportCommittedIsUnblockedAndMarkedImported()
        {
            var download = Awaiting("blocked", DownloadStatus.ImportBlocked);
            download.Blocked("Unable to import the download", "No importable files found");
            JobsFor("blocked", completed: true, committed: true);

            await Build(download).RunCycleAsync(CancellationToken.None);

            Assert.Equal(DownloadStatus.Moved, download.Status);
            // The reason it was blocked must not survive on a record now called imported.
            Assert.Null(download.ImportBlockReason);
            Assert.Single(_saved);
        }

        [Fact]
        public async Task ADownloadStillBeingImportedIsLeftAlone()
        {
            var download = Awaiting("running", DownloadStatus.ImportPending);
            // The checkpoint is written in the committing transaction, so a job carrying
            // it but not yet complete has not finished.
            JobsFor("running", completed: false, committed: true);

            await Build(download).RunCycleAsync(CancellationToken.None);

            Assert.Equal(DownloadStatus.ImportPending, download.Status);
            Assert.Empty(_saved);
        }

        [Fact]
        public async Task ADownloadWhoseJobFinishedWithoutCommittingIsLeftAlone()
        {
            var download = Awaiting("failed", DownloadStatus.ImportBlocked);
            JobsFor("failed", completed: true, committed: false);

            await Build(download).RunCycleAsync(CancellationToken.None);

            Assert.Equal(DownloadStatus.ImportBlocked, download.Status);
            Assert.Empty(_saved);
        }

        [Fact]
        public async Task ADownloadWithNoJobAtAllIsLeftAlone()
        {
            var download = Awaiting("orphan", DownloadStatus.ImportPending);
            _jobs.Setup(s => s.GetJobsForDownloadAsync("orphan")).ReturnsAsync([]);

            await Build(download).RunCycleAsync(CancellationToken.None);

            Assert.Equal(DownloadStatus.ImportPending, download.Status);
            Assert.Empty(_saved);
        }

        /// <summary>One row that cannot be put right must not strand the rest.</summary>
        [Fact]
        public async Task OneUnrecoverableRowDoesNotStopTheOthers()
        {
            var bad = Awaiting("bad", DownloadStatus.ImportPending);
            var good = Awaiting("good", DownloadStatus.ImportPending);
            _jobs.Setup(s => s.GetJobsForDownloadAsync("bad")).ThrowsAsync(new InvalidOperationException("boom"));
            JobsFor("good", completed: true, committed: true);

            await Build(bad, good).RunCycleAsync(CancellationToken.None);

            Assert.Equal(DownloadStatus.ImportPending, bad.Status);
            Assert.Equal(DownloadStatus.Moved, good.Status);
            Assert.Single(_saved);
        }

        [Fact]
        public async Task NothingAwaitingImportCostsNoWork()
        {
            await Build().RunCycleAsync(CancellationToken.None);

            Assert.Empty(_saved);
            _jobs.Verify(s => s.GetJobsForDownloadAsync(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// NZBGet holds par2 paused; once the content finishes, every file left in the
        /// group is paused, so the group reads as paused and is never post-processed. The
        /// download is done and nothing but a resume will let the client say so.
        /// </summary>
        [Fact]
        public async Task ADownloadLeftPausedAtTheClientIsResumed()
        {
            var download = Awaiting("paused", DownloadStatus.Paused);
            _gateway
                .Setup(g => g.ResumeAsync(It.IsAny<DownloadClientConfiguration>(), download, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            await Build(download).RunCycleAsync(CancellationToken.None);

            _gateway.Verify(
                g => g.ResumeAsync(It.IsAny<DownloadClientConfiguration>(), download, It.IsAny<CancellationToken>()),
                Times.Once);
            // Nothing is claimed about the import here: resuming lets the client finish,
            // and the import follows on its own.
            Assert.Equal(DownloadStatus.Paused, download.Status);
        }

        [Fact]
        public async Task ADownloadThatOnlyJustPausedIsGivenTimeFirst()
        {
            var download = Awaiting("fresh", DownloadStatus.Paused, age: TimeSpan.FromMinutes(5));

            await Build(download).RunCycleAsync(CancellationToken.None);

            _gateway.Verify(
                g => g.ResumeAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<Download>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>A client that ignores the nudge must not be asked on every cycle.</summary>
        [Fact]
        public async Task APausedDownloadIsNotNudgedAgainStraightAway()
        {
            var download = Awaiting("stubborn", DownloadStatus.Paused);
            _gateway
                .Setup(g => g.ResumeAsync(It.IsAny<DownloadClientConfiguration>(), download, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var processor = Build(download);
            await processor.RunCycleAsync(CancellationToken.None);
            await processor.RunCycleAsync(CancellationToken.None);

            _gateway.Verify(
                g => g.ResumeAsync(It.IsAny<DownloadClientConfiguration>(), download, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task APausedDownloadWithNoClientIsLeftAlone()
        {
            var download = Awaiting("clientless", DownloadStatus.Paused);
            download.DownloadClientId = null;

            await Build(download).RunCycleAsync(CancellationToken.None);

            _gateway.Verify(
                g => g.ResumeAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<Download>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>A paused download never commits an import, so it must not be "recovered".</summary>
        [Fact]
        public async Task APausedDownloadIsNeverMarkedImported()
        {
            var download = Awaiting("paused-not-imported", DownloadStatus.Paused);
            JobsFor("paused-not-imported", completed: true, committed: true);
            _gateway
                .Setup(g => g.ResumeAsync(It.IsAny<DownloadClientConfiguration>(), download, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            await Build(download).RunCycleAsync(CancellationToken.None);

            Assert.NotEqual(DownloadStatus.Moved, download.Status);
        }
    }
}
