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
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Application.FoundBooks.Models;
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Domain.FoundBooks;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.FoundBooks
{
    /// <summary>
    /// How one scan's findings become rows: what is offered, what waits, what is
    /// forgotten, and what an operator's decision protects.
    /// </summary>
    [Trait("Name", "FoundBookScanServiceTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookScanServiceTests : BaseTests
    {
        private const string Watch = "/downloads/completed";

        private readonly Mock<IHubBroadcaster> _broadcaster = new();
        private readonly FakeScanner _scanner = new();
        private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        private IFoundBookRepository _repository = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _repository = _provider.GetRequiredService<IFoundBookRepository>();
        }

        private FoundBookScanService BuildService(params string[] folders) => new(
            new FakeResolver(folders.Length == 0 ? [Watch] : folders),
            _scanner,
            _repository,
            _audiobookRepository,
            _downloadRepository,
            _broadcaster.Object,
            _clock,
            NullLogger<FoundBookScanService>.Instance);

        private FoundBookCandidate Candidate(
            string key,
            string signature = "sig-1",
            TimeSpan? age = null,
            bool inProgress = false,
            string title = "Wool",
            string author = "Hugh Howey") => new(
            Watch,
            $"{Watch}/{key}",
            key,
            signature,
            [new FoundBookFileEntry($"{Watch}/{key}/01.mp3", 100, _clock.GetUtcNow().UtcDateTime - (age ?? TimeSpan.FromHours(1)), true,
                new FoundBookProbeSnapshot(true, null, 600, 0, 1, 1, null, title, author, null, null, null, null, null, null, null))],
            title,
            author,
            null,
            null,
            null,
            null,
            null,
            FoundBookCompleteness.Complete,
            "One file.",
            _clock.GetUtcNow().UtcDateTime - (age ?? TimeSpan.FromHours(1)),
            inProgress);

        [Fact]
        public async Task SettledCluster_IsOfferedOnFirstSight()
        {
            _scanner.Next = [Candidate("a")];

            var summary = await BuildService().ScanAllAsync();

            var row = Assert.Single(await _repository.GetAllAsync());
            Assert.Equal(FoundBookState.Pending, row.State);
            Assert.Equal("Wool", row.DetectedTitle);
            Assert.Equal(1, summary.Pending);
            _broadcaster.Verify(b => b.BroadcastAsync(RealtimeHubTarget.Settings, FoundBookScanService.ChangedEvent, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FreshCluster_WaitsUntilASecondScanSeesItUnchanged()
        {
            _scanner.Next = [Candidate("a", age: TimeSpan.FromMinutes(1))];
            var service = BuildService();

            await service.ScanAllAsync();
            var first = Assert.Single(await _repository.GetAllAsync());
            Assert.Equal(FoundBookState.Blocked, first.State);
            Assert.Contains("still changing", first.BlockedReason);

            // Same signature next time: settled, whatever its age.
            _clock.Advance(TimeSpan.FromMinutes(1));
            _scanner.Next = [Candidate("a", age: TimeSpan.FromMinutes(2))];
            await service.ScanAllAsync();

            var second = Assert.Single(await _repository.GetAllAsync());
            Assert.Equal(first.Id, second.Id);
            Assert.Equal(FoundBookState.Pending, second.State);
        }

        [Fact]
        public async Task ChangedSignature_SendsAnOfferedClusterBackToBlocked()
        {
            _scanner.Next = [Candidate("a", signature: "sig-1")];
            var service = BuildService();
            await service.ScanAllAsync();

            _scanner.Next = [Candidate("a", signature: "sig-2", age: TimeSpan.FromSeconds(30))];
            await service.ScanAllAsync();

            var row = Assert.Single(await _repository.GetAllAsync());
            Assert.Equal(FoundBookState.Blocked, row.State);
            Assert.Equal(_clock.GetUtcNow().UtcDateTime, row.SignatureChangedAt);
        }

        [Fact]
        public async Task DownloadInProgress_Blocks()
        {
            _scanner.Next = [Candidate("a", inProgress: true)];

            await BuildService().ScanAllAsync();

            var row = Assert.Single(await _repository.GetAllAsync());
            Assert.Equal(FoundBookState.Blocked, row.State);
            Assert.Contains("download is still in progress", row.BlockedReason);
        }

        [Fact]
        public async Task VanishedCluster_IsForgotten()
        {
            _scanner.Next = [Candidate("a"), Candidate("b")];
            var service = BuildService();
            await service.ScanAllAsync();

            _scanner.Next = [Candidate("b")];
            await service.ScanAllAsync();

            var row = Assert.Single(await _repository.GetAllAsync());
            Assert.Equal("b", row.ClusterKey);
        }

        [Fact]
        public async Task IgnoredCluster_KeepsItsDecisionAcrossScans()
        {
            _scanner.Next = [Candidate("a")];
            var service = BuildService();
            await service.ScanAllAsync();
            var row = Assert.Single(await _repository.GetAllAsync());
            await _repository.UpdateAsync(row.Id, r => r.State = FoundBookState.Ignored);

            _scanner.Next = [Candidate("a", signature: "sig-2")];
            await service.ScanAllAsync();

            var after = Assert.Single(await _repository.GetAllAsync());
            Assert.Equal(FoundBookState.Ignored, after.State);
            Assert.Equal("sig-2", after.Signature);
        }

        [Fact]
        public async Task RowsForAFolderNoLongerWatched_AreDeleted()
        {
            _scanner.Next = [Candidate("a")];
            await BuildService().ScanAllAsync();
            Assert.Single(await _repository.GetAllAsync());

            _scanner.Next = [];
            await BuildService("/somewhere/else").ScanAllAsync();

            Assert.Empty(await _repository.GetAllAsync());
        }

        [Fact]
        public async Task RowsForAnUnavailableFolder_AreKept()
        {
            _scanner.Next = [Candidate("a")];
            await BuildService().ScanAllAsync();
            var row = Assert.Single(await _repository.GetAllAsync());
            await _repository.UpdateAsync(row.Id, r => r.State = FoundBookState.Ignored);

            // The mount went away: the folder resolves to nothing but is still configured.
            var service = new FoundBookScanService(
                new FakeResolver([], unavailable: [Watch]),
                _scanner,
                _repository,
                _audiobookRepository,
                _downloadRepository,
                _broadcaster.Object,
                _clock,
                NullLogger<FoundBookScanService>.Instance);
            await service.ScanAllAsync();

            var kept = Assert.Single(await _repository.GetAllAsync());
            Assert.Equal(FoundBookState.Ignored, kept.State);
        }

        [Fact]
        public async Task FailedWalk_LeavesLastTimesRowsAlone()
        {
            _scanner.Next = [Candidate("a")];
            var service = BuildService();
            await service.ScanAllAsync();

            _scanner.Next = [];
            _scanner.FailNext = true;
            await service.ScanAllAsync();

            Assert.Single(await _repository.GetAllAsync());
        }

        [Fact]
        public async Task ClusterInsideAnActiveDownload_IsBlockedAsOwned()
        {
            await _downloadRepository.AddAsync(new Download
            {
                Title = "Wool pack",
                Status = DownloadStatus.ImportPending,
                FinalPath = $"{Watch}/a"
            });
            _scanner.Next = [Candidate("a")];

            await BuildService().ScanAllAsync();

            var row = Assert.Single(await _repository.GetAllAsync());
            Assert.Equal(FoundBookState.Blocked, row.State);
            Assert.Equal(FoundBookBlockedKind.OwnedByDownload, row.BlockedKind);
            Assert.Contains("Wool pack", row.BlockedReason);
        }

        [Fact]
        public async Task ClusterOfAnImportBlockedDownload_IsOffered()
        {
            // A failed import is exactly the leftover this feature is for.
            await _downloadRepository.AddAsync(new Download
            {
                Title = "Wool pack",
                Status = DownloadStatus.ImportBlocked,
                FinalPath = $"{Watch}/a"
            });
            _scanner.Next = [Candidate("a")];

            await BuildService().ScanAllAsync();

            Assert.Equal(FoundBookState.Pending, Assert.Single(await _repository.GetAllAsync()).State);
        }

        [Fact]
        public async Task LibraryStatus_ComesFromTheLibrary()
        {
            var held = new Listenarr.Tests.Builders.AudiobookBuilder().WithTitle("Wool").WithAuthor("Hugh Howey").WithFilePath("/lib/wool.m4b").Build();
            await _audiobookRepository.AddAsync(held);
            _scanner.Next = [Candidate("a", title: "Wool", author: "Hugh Howey"), Candidate("b", title: "Shift", author: "Hugh Howey")];

            await BuildService().ScanAllAsync();

            var rows = (await _repository.GetAllAsync()).OrderBy(r => r.ClusterKey).ToList();
            Assert.Equal(FoundBookLibraryStatus.InLibrary, rows[0].LibraryStatus);
            Assert.Equal(held.Id, rows[0].MatchedAudiobookId);
            Assert.Equal(FoundBookLibraryStatus.New, rows[1].LibraryStatus);
        }

        [Fact]
        public async Task PreviousProbes_AreHandedBackToTheScanner()
        {
            _scanner.Next = [Candidate("a")];
            var service = BuildService();
            await service.ScanAllAsync();

            _scanner.Next = [Candidate("a")];
            await service.ScanAllAsync();

            Assert.Contains($"{Watch}/a/01.mp3", _scanner.LastKnownFiles.Keys);
        }

        private sealed class FakeResolver(IReadOnlyList<string> folders, IReadOnlyList<string>? unavailable = null) : IFoundBookWatchFolderResolver
        {
            public Task<FoundBookWatchFolders> ResolveAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new FoundBookWatchFolders(
                    folders.Select(f => new FoundBookWatchFolder(f, FileSystemPathSemantics.CurrentHostDefault)).ToList(),
                    unavailable ?? [],
                    [],
                    true));
        }

        private sealed class FakeScanner : IFoundBookScanner
        {
            public IReadOnlyList<FoundBookCandidate> Next { get; set; } = [];
            public bool FailNext { get; set; }
            public IReadOnlyDictionary<string, FoundBookKnownFile> LastKnownFiles { get; private set; } =
                new Dictionary<string, FoundBookKnownFile>();

            public Task<FoundBookScanReport> ScanAsync(
                FoundBookWatchFolder watchFolder,
                IReadOnlyDictionary<string, FoundBookKnownFile> knownFiles,
                CancellationToken cancellationToken = default)
            {
                LastKnownFiles = knownFiles;
                if (FailNext)
                {
                    FailNext = false;
                    return Task.FromResult(new FoundBookScanReport(watchFolder.Path, [], ["could not list"], Succeeded: false));
                }

                return Task.FromResult(new FoundBookScanReport(watchFolder.Path, Next.Where(c => c.WatchFolder == watchFolder.Path).ToList(), []));
            }
        }

        private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
        {
            private DateTimeOffset _utcNow = utcNow;

            public override DateTimeOffset GetUtcNow() => _utcNow;

            public void Advance(TimeSpan duration) => _utcNow += duration;
        }
    }
}
