using System.Reflection;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Monitoring
{
    [Trait("Name", "DownloadMonitorServiceTests")]
    [Trait("Category", "DownloadMonitorService")]
    public class DownloadMonitorServiceTests : BaseTests
    {
        private DownloadMonitorService downloadMonitorService = null!;
        private MethodInfo monitorDownloadsAsync = null!;
        private DownloadClientConfiguration client = null!;
        private DownloadClientConfiguration disabledClient = null!;

        public override async Task InitializeAsync()
        {
            downloadMonitorService = _provider.GetRequiredService<DownloadMonitorService>();
            var method = typeof(DownloadMonitorService).GetMethod("MonitorDownloadsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            monitorDownloadsAsync = method;

            client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("mock")
                .WithName("Mock")
                .Build());

            disabledClient = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("mock")
                .WithName("Mock")
                .WithDisabled()
                .Build());
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_NoDownload_NoResult()
        {
            var downloads = await _downloadRepository.GetAllAsync();
            Assert.Empty(downloads);

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            downloads = await _downloadRepository.GetAllAsync();
            Assert.Empty(downloads);

            var downloadProcessingJobService = _provider.GetRequiredService<IDownloadProcessingJobService>();
            var job = await downloadProcessingJobService.GetNextJobAsync();
            Assert.Null(job);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_CompletedStaysCompleted()
        {
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithCompletedStatus(DateTime.UtcNow)
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Completed, download.Status);

            var downloadProcessingJobService = _provider.GetRequiredService<IDownloadProcessingJobService>();
            var job = await downloadProcessingJobService.GetNextJobAsync();
            Assert.Null(job);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Completed, download.Status);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_DownloadingBecomesCompleted()
        {
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("1")
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Downloading, download.Status);
            Assert.True(download.Progress > 50);

            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Downloading, download.Status);
            Assert.True(download.Progress > 80);

            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Completed, download.Status);
            Assert.True(download.Progress >= 100);

            var downloadProcessingJobService = _provider.GetRequiredService<IDownloadProcessingJobService>();
            var job = await downloadProcessingJobService.GetNextJobAsync();
            Assert.NotNull(job);
            Assert.Equal(download.Id, job.DownloadId);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_LiveSnapshot_RemovesEligibleOrphanedDownload()
        {
            var orphan = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithId("orphaned-download")
                .WithDownloading(0)
                .WithExternalId("missing-client-id")
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithDownloadClientConfiguration(client)
                .Build());
            var present = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithId("present-download")
                .WithDownloading(0)
                .WithExternalId("1")
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            Assert.Null(await _downloadRepository.GetByIdAsync(orphan.Id));
            Assert.NotNull(await _downloadRepository.GetByIdAsync(present.Id));
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_LiveSnapshot_RemovesOldActiveClientDownloadWithoutExternalId()
        {
            var unlinked = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithId("unlinked-download")
                .WithDownloading(0)
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            Assert.Null(await _downloadRepository.GetByIdAsync(unlinked.Id));
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_OrphanCleanup_DoesNotRunEveryCycle()
        {
            var adapter = _provider.GetServices<IDownloadClientAdapter>()
                .OfType<DownloadCLientAdapterMock>()
                .Single();
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("1")
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            Assert.Equal(2, adapter.FilteredQueueRequestCount);
            Assert.Equal(1, adapter.FullSnapshotQueueRequestCount);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_OrphanCleanup_RunsAgainAfterThrottleInterval()
        {
            var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
            Init(services => services.WithSingleton<TimeProvider>(timeProvider));
            downloadMonitorService = _provider.GetRequiredService<DownloadMonitorService>();
            client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("mock")
                .WithName("Mock")
                .Build());
            var adapter = _provider.GetServices<IDownloadClientAdapter>()
                .OfType<DownloadCLientAdapterMock>()
                .Single();
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("1")
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            timeProvider.Advance(TimeSpan.FromMinutes(9));
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            timeProvider.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            Assert.Equal(3, adapter.FilteredQueueRequestCount);
            Assert.Equal(2, adapter.FullSnapshotQueueRequestCount);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_RespectSchedulingInterval()
        {
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("1")
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Downloading, download.Status);
            Assert.True(download.Progress == 10);

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Downloading, download.Status);
            Assert.True(download.Progress == 10);

            var downloadProcessingJobService = _provider.GetRequiredService<IDownloadProcessingJobService>();
            var job = await downloadProcessingJobService.GetNextJobAsync();
            Assert.Null(job);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_DisabledClientDownload_DoesNotUpdate()
        {
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("DISABLED_1")
                .WithDownloadClientConfiguration(disabledClient)
                .Build());
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("DISABLED_2")
                .WithDownloadClientConfiguration(disabledClient)
                .Build());
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("1")
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            var downloads = await _downloadRepository.GetAllAsync();
            Assert.Equal(3, downloads.Count);

            foreach (Download download in downloads)
            {
                if (download.DownloadClientId == client.Id)
                {
                    Assert.Equal(10, download.Progress);
                }
                else
                {
                    Assert.Equal(0, download.Progress);
                    Assert.Equal(DownloadStatus.Downloading, download.Status);
                }
            }

            var downloadProcessingJobService = _provider.GetRequiredService<IDownloadProcessingJobService>();
            var job = await downloadProcessingJobService.GetNextJobAsync();
            Assert.Null(job);
        }

        [Fact]
        public void OnDownloadFailed_Nzbget_DoesNotRemoveClientHistory_WhenFailedHandlingEnabled()
        {
            var client = new DownloadClientConfiguration
            {
                Id = "nzbget-1",
                Type = "nzbget"
            };

            Assert.False(DownloadMonitorProcessor.ShouldRemoveFailedClientItem(client));
        }

        [Fact]
        public void OnDownloadFailed_Qbittorrent_RemovesFailedClientItem_WhenFailedHandlingEnabled()
        {
            var client = new DownloadClientConfiguration
            {
                Id = "qbittorrent-1",
                Type = "qbittorrent"
            };

            Assert.True(DownloadMonitorProcessor.ShouldRemoveFailedClientItem(client));
        }

        /// <summary>
        /// A local failure is still left alone: another release would be moved to the same
        /// place and fail there too.
        /// </summary>
        [Fact]
        public void OnDownloadFailed_NzbgetMoveFailure_IsLeftForAPerson()
        {
            var download = Failed("FAILURE/MOVE");

            var reason = DownloadMonitorProcessor.ResolveFailureReason(
                Nzbget,
                download,
                "NZBGet failed during post-processing or final move.");

            Assert.Equal(DownloadFailureReason.PostProcessing, reason);
            Assert.False(DownloadFailurePolicy.For(reason).SearchAgain);
        }

        /// <summary>
        /// These three used to suppress the search, on the grounds that grabbing again
        /// found the same release and NZBGet discarded it. Both halves are now dealt with
        /// - the release is blocked so the search reaches a different one, and the record
        /// that caused the discard is deleted - so the search is what they get. 183 of the
        /// 195 books stranded by the old rule had a release available when asked.
        /// </summary>
        [Theory]
        [InlineData("DELETED/COPY", DownloadFailureReason.Duplicate, false)]
        [InlineData("DELETED/DUPE", DownloadFailureReason.Duplicate, false)]
        // The password belongs to the post, so a wrong one says the NZB is the wrong
        // release - which is a reason to block it and look for the right one.
        [InlineData("WARNING/PASSWORD", DownloadFailureReason.Password, true)]
        public void OnDownloadFailed_AFailureARepeatCannotCure_ClearsTheWayAndSearchesAgain(
            string status,
            DownloadFailureReason expected,
            bool blocksTheRelease)
        {
            var download = Failed(status);

            var reason = DownloadMonitorProcessor.ResolveFailureReason(
                Nzbget,
                download,
                "NZBGet discarded the download.");
            var response = DownloadFailurePolicy.For(reason);

            Assert.Equal(expected, reason);
            Assert.True(response.SearchAgain);
            Assert.True(response.PurgeClientRecord);
            Assert.Equal(blocksTheRelease, response.BlockRelease);
        }

        /// <summary>
        /// The classification is made when the client is read, so a download that already
        /// carries one is taken at its word rather than re-derived from a raw status that
        /// may not be there.
        /// </summary>
        [Fact]
        public void OnDownloadFailed_AReasonAlreadyOnTheRecordIsUsed()
        {
            var download = new DownloadBuilder().Build();
            download.Failed("whatever the client said", DownloadFailureReason.Damaged);

            Assert.Equal(
                DownloadFailureReason.Damaged,
                DownloadMonitorProcessor.ResolveFailureReason(Nzbget, download, "whatever the client said"));
        }

        /// <summary>
        /// A download that failed before the classification existed carries no reason, and
        /// the raw status NZBGet gave is still on it. Reading that is what lets the backlog
        /// be understood rather than re-failed.
        /// </summary>
        [Fact]
        public void OnDownloadFailed_AnOlderRowIsClassifiedFromItsRawStatus()
        {
            var download = Failed("FAILURE/HEALTH");
            download.FailureReason = null;

            Assert.Equal(
                DownloadFailureReason.Health,
                DownloadMonitorProcessor.ResolveFailureReason(Nzbget, download, "health too low"));
        }

        /// <summary>
        /// A client that names no failure of its own must not have NZBGet's vocabulary
        /// read into whatever it said.
        /// </summary>
        [Fact]
        public void OnDownloadFailed_AnotherClientsMessageIsNotReadAsNzbgetStatus()
        {
            var download = new DownloadBuilder().Build();
            download.Metadata["ClientFailureReason"] = "FAILURE/HEALTH";

            var reason = DownloadMonitorProcessor.ResolveFailureReason(
                new DownloadClientConfiguration { Id = "qbittorrent-1", Type = "qbittorrent" },
                download,
                "errored");

            Assert.Equal(DownloadFailureReason.Unknown, reason);
        }

        private static DownloadClientConfiguration Nzbget => new()
        {
            Id = "nzbget-1",
            Type = "nzbget"
        };

        private static Download Failed(string rawStatus)
        {
            var download = new DownloadBuilder().Build();
            download.Metadata["ClientFailureReason"] = rawStatus;
            return download;
        }

        private sealed class MutableTimeProvider(DateTimeOffset currentTime) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => currentTime;

            public void Advance(TimeSpan value) => currentTime = currentTime.Add(value);
        }
    }
}
