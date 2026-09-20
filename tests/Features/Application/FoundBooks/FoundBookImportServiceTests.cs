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
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Application.Search.Strategies;
using Listenarr.Domain.FoundBooks;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.FoundBooks
{
    /// <summary>
    /// A person's Add as a queue the row holds: what is refused before the row is
    /// marked, what the worker runs, what it retries, and what it leaves on the row.
    /// The decision service is real, so the row's fields are the ones a restart
    /// would find.
    /// </summary>
    [Trait("Name", "FoundBookImportServiceTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookImportServiceTests : BaseTests
    {
        private readonly Mock<ISearchService> _search = new();
        private readonly Mock<IMetadataStrategy> _strategy = new();
        private readonly Mock<IFoundBookImportRunner> _runner = new();
        private readonly Mock<IFileSystem> _fileSystem = new();
        private readonly Mock<IFoundBookScanProcessor> _scanProcessor = new();
        private readonly Mock<IFileSystemSemanticsResolver> _semantics = new();
        private readonly FoundBookImportSignal _signal = new();
        private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
        private IFoundBookRepository _repository = null!;
        private FoundBookDecisionService _decisions = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _repository = _provider.GetRequiredService<IFoundBookRepository>();
            _decisions = new FoundBookDecisionService(
                _repository,
                new FoundBookCleanup(_fileSystem.Object, _rootFolderRepository, NullLogger<FoundBookCleanup>.Instance),
                _fileSystem.Object,
                _semantics.Object,
                _historyRepository,
                _scanProcessor.Object,
                _clock,
                NullLogger<FoundBookDecisionService>.Instance);

            var source = new ApiConfiguration { Name = "Audible", BaseUrl = "https://api.audible.com" };
            _search.Setup(s => s.GetEnabledMetadataSourcesAsync()).ReturnsAsync([source]);
            _strategy.Setup(s => s.CanHandle(source)).Returns(true);
            _strategy.Setup(s => s.FetchMetadataAsync("B00ABCDEF1", source, null, It.IsAny<string?>()))
                .ReturnsAsync(new AudibleBookMetadata { Asin = "B00ABCDEF1", Title = "Wool" });
            GivenRunnerFinishesWith(row => FoundBookImportResult.Ok(42, row));
        }

        private void GivenRunnerFinishesWith(Func<FoundBook, FoundBookImportResult> outcome) =>
            _runner.Setup(r => r.RunAsync(It.IsAny<FoundBook>(), It.IsAny<AudibleBookMetadata>(), It.IsAny<FoundBookImportOptions>(), It.IsAny<CancellationToken>()))
                .Returns(async (FoundBook row, AudibleBookMetadata _, FoundBookImportOptions _, CancellationToken ct) =>
                {
                    // The real runner ends the row's importing state one way or the other.
                    var result = outcome(row);
                    if (result.Success)
                    {
                        await _repository.UpdateAsync(row.Id, r => { r.State = FoundBookState.Imported; r.ImportRequestJson = null; r.ImportStartedAt = null; }, ct);
                    }
                    else
                    {
                        await _decisions.AbortImportAsync(row.Id, result.Error, ct);
                    }

                    return result with { Book = await _repository.GetAsync(row.Id, ct) };
                });

        private async Task<FoundBook> GivenRow(string folder = "/downloads/Wool", string? matchAsin = "B00ABCDEF1") =>
            await _repository.AddAsync(new FoundBook
            {
                ClusterKey = Guid.NewGuid().ToString("N"),
                Signature = "sig",
                WatchFolder = "/downloads",
                BookFolder = folder,
                FilesJson = FoundBookFilesJson.Serialize([new FoundBookFileEntry($"{folder}/01.mp3", 1, DateTime.UtcNow, true, null)]),
                AudioFileCount = 1,
                DetectedTitle = "Wool",
                MatchAsin = matchAsin,
                State = FoundBookState.Pending
            });

        private Task GivenDefaultRoot() =>
            _rootFolderRepository.AddAsync(new RootFolderBuilder().WithPath("/library").WithIsDefault().Build());

        private FoundBookImportService Build() => new(
            _repository,
            _rootFolderRepository,
            _search.Object,
            new MetadataStrategyCoordinator([_strategy.Object], NullLogger<MetadataStrategyCoordinator>.Instance),
            _provider.GetRequiredService<IConfigurationService>(),
            _decisions,
            _runner.Object,
            _signal,
            new NoopHubBroadcaster(),
            _clock,
            NullLogger<FoundBookImportService>.Instance);

        private static FoundBookManualImportRequest Request(string? asin = null, string? root = null, bool monitored = true, bool separate = false) =>
            new(asin, root, monitored, separate);

        [Fact]
        public async Task Enqueue_MarksTheRowWithTheResolvedRequest_AndWakesTheWorker()
        {
            await GivenDefaultRoot();
            var row = await GivenRow();

            var result = await Build().EnqueueAsync(row.Id, Request());

            Assert.True(result.Success, result.Error);
            Assert.Null(result.AudiobookId);
            var queued = (await _repository.GetAsync(row.Id))!;
            Assert.Equal(FoundBookState.Importing, queued.State);
            Assert.Contains("B00ABCDEF1", queued.ImportRequestJson);
            Assert.Contains("/library", queued.ImportRequestJson);
            Assert.Equal(0, queued.ImportAttempts);
            Assert.NotNull(queued.ImportStartedAt);
            Assert.True(await _signal.WaitAsync(TimeSpan.Zero));
            _runner.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Enqueue_RefusesWithoutAMatch_OrARoot_OrAnOfferedRow_LeavingTheRowAlone()
        {
            var noMatch = await GivenRow(folder: "/downloads/A", matchAsin: null);
            var noRoot = await GivenRow(folder: "/downloads/B");

            var noMatchResult = await Build().EnqueueAsync(noMatch.Id, Request());
            var noRootResult = await Build().EnqueueAsync(noRoot.Id, Request());

            Assert.Equal(FoundBookImportFailure.NoMatch, noMatchResult.Failure);
            Assert.Equal(FoundBookImportFailure.AddRefused, noRootResult.Failure);
            Assert.Equal(FoundBookState.Pending, (await _repository.GetAsync(noMatch.Id))!.State);
            Assert.Equal(FoundBookState.Pending, (await _repository.GetAsync(noRoot.Id))!.State);

            await GivenDefaultRoot();
            await _repository.UpdateAsync(noRoot.Id, r => r.State = FoundBookState.Ignored);
            var ignored = await Build().EnqueueAsync(noRoot.Id, Request());
            Assert.Equal(FoundBookImportFailure.WrongState, ignored.Failure);
            Assert.Equal(FoundBookImportFailure.NotFound, (await Build().EnqueueAsync(999, Request())).Failure);
            Assert.False(await _signal.WaitAsync(TimeSpan.Zero));
        }

        [Fact]
        public async Task RunDue_RunsTheQueuedRequest_WithWhatWasQueued()
        {
            await GivenDefaultRoot();
            var row = await GivenRow(matchAsin: "B00OLDMATCH");
            var service = Build();
            await service.EnqueueAsync(row.Id, Request(asin: "B00ABCDEF1", root: "/other", monitored: false, separate: true));

            var drain = await service.RunDueAsync();

            Assert.Equal(1, drain.Ran);
            Assert.Null(drain.NextDueAt);
            _runner.Verify(r => r.RunAsync(
                It.Is<FoundBook>(b => b.Id == row.Id && b.State == FoundBookState.Importing),
                It.Is<AudibleBookMetadata>(m => m.Asin == "B00ABCDEF1"),
                It.Is<FoundBookImportOptions>(o => o.RootPath == "/other" && !o.Monitored && o.AllowDuplicateEdition && o.IncludeCompanions && !o.AutoAdded),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(FoundBookState.Imported, (await _repository.GetAsync(row.Id))!.State);
        }

        [Fact]
        public async Task RunDue_TakesRowsOldestFirst_AndOnlyQueuedOnes()
        {
            await GivenDefaultRoot();
            var second = await GivenRow(folder: "/downloads/B");
            var first = await GivenRow(folder: "/downloads/A");
            var scannersOwn = await GivenRow(folder: "/downloads/C");
            await _repository.UpdateAsync(scannersOwn.Id, r => r.State = FoundBookState.Importing);
            var service = Build();
            await service.EnqueueAsync(first.Id, Request());
            _clock.Advance(TimeSpan.FromSeconds(1));
            await service.EnqueueAsync(second.Id, Request());

            var ran = new List<int>();
            _runner.Setup(r => r.RunAsync(It.IsAny<FoundBook>(), It.IsAny<AudibleBookMetadata>(), It.IsAny<FoundBookImportOptions>(), It.IsAny<CancellationToken>()))
                .Returns(async (FoundBook row, AudibleBookMetadata _, FoundBookImportOptions _, CancellationToken ct) =>
                {
                    ran.Add(row.Id);
                    await _repository.UpdateAsync(row.Id, r => { r.State = FoundBookState.Imported; r.ImportRequestJson = null; }, ct);
                    return FoundBookImportResult.Ok(42, row);
                });

            var drain = await service.RunDueAsync();

            Assert.Equal(2, drain.Ran);
            Assert.Equal([first.Id, second.Id], ran);
            Assert.Equal(FoundBookState.Importing, (await _repository.GetAsync(scannersOwn.Id))!.State);
        }

        [Fact]
        public async Task ADatabaseFailure_IsRequeuedWithAWait_ThenGivenUpOn()
        {
            await GivenDefaultRoot();
            var row = await GivenRow();
            var service = Build();
            await service.EnqueueAsync(row.Id, Request());
            GivenRunnerFinishesWith(_ => FoundBookImportResult.Fail(FoundBookImportFailure.Persistence, "The database was unavailable."));

            var first = await service.RunDueAsync();
            var afterFirst = (await _repository.GetAsync(row.Id))!;
            Assert.Equal(1, first.Ran);
            Assert.Equal(FoundBookState.Importing, afterFirst.State);
            Assert.Equal(1, afterFirst.ImportAttempts);
            Assert.Equal(_clock.GetUtcNow().UtcDateTime + FoundBookImportService.RetryDelays[0], afterFirst.ImportNotBefore);
            Assert.Equal(afterFirst.ImportNotBefore, first.NextDueAt);

            // Not due yet: nothing runs.
            var early = await service.RunDueAsync();
            Assert.Equal(0, early.Ran);
            Assert.Equal(afterFirst.ImportNotBefore, early.NextDueAt);

            _clock.Advance(FoundBookImportService.RetryDelays[0]);
            var second = await service.RunDueAsync();
            var afterSecond = (await _repository.GetAsync(row.Id))!;
            Assert.Equal(1, second.Ran);
            Assert.Equal(FoundBookState.Importing, afterSecond.State);
            Assert.Equal(2, afterSecond.ImportAttempts);

            _clock.Advance(FoundBookImportService.RetryDelays[1]);
            var third = await service.RunDueAsync();
            var final = (await _repository.GetAsync(row.Id))!;
            Assert.Equal(1, third.Ran);
            Assert.Null(third.NextDueAt);
            Assert.Equal(FoundBookState.Pending, final.State);
            Assert.Null(final.ImportRequestJson);
            Assert.Equal("The database was unavailable.", final.LastImportError);
            _runner.Verify(r => r.RunAsync(It.IsAny<FoundBook>(), It.IsAny<AudibleBookMetadata>(), It.IsAny<FoundBookImportOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        [Fact]
        public async Task AnyOtherFailure_IsNotRetried_AndItsReasonStaysOnTheRow()
        {
            await GivenDefaultRoot();
            var row = await GivenRow();
            var service = Build();
            await service.EnqueueAsync(row.Id, Request());
            GivenRunnerFinishesWith(_ => FoundBookImportResult.Fail(FoundBookImportFailure.ImportFailed, "disk full"));

            var drain = await service.RunDueAsync();

            Assert.Equal(1, drain.Ran);
            Assert.Null(drain.NextDueAt);
            var after = (await _repository.GetAsync(row.Id))!;
            Assert.Equal(FoundBookState.Pending, after.State);
            Assert.Equal("disk full", after.LastImportError);
            Assert.Null(after.ImportRequestJson);
        }

        [Fact]
        public async Task AnAsinTheCatalogueDoesNotKnow_PutsTheRowBackWithTheReason()
        {
            await GivenDefaultRoot();
            var row = await GivenRow(matchAsin: "B00UNKNOWN1");
            var service = Build();
            await service.EnqueueAsync(row.Id, Request());

            await service.RunDueAsync();

            var after = (await _repository.GetAsync(row.Id))!;
            Assert.Equal(FoundBookState.Pending, after.State);
            Assert.Contains("B00UNKNOWN1", after.LastImportError);
            _runner.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ARowSharingItsFolder_ImportsWithoutCompanions()
        {
            await GivenDefaultRoot();
            var row = await GivenRow(folder: "/downloads/pack");
            await GivenRow(folder: "/downloads/pack");
            var service = Build();
            await service.EnqueueAsync(row.Id, Request());

            await service.RunDueAsync();

            _runner.Verify(r => r.RunAsync(
                It.IsAny<FoundBook>(), It.IsAny<AudibleBookMetadata>(),
                It.Is<FoundBookImportOptions>(o => !o.IncludeCompanions),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ARequestQueuedBeforeARestart_IsRunOnTheFirstPass()
        {
            // No Enqueue in this process: the row carries the request from an earlier one.
            await GivenDefaultRoot();
            var row = await GivenRow();
            await _decisions.BeginImportAsync(row.Id, new FoundBookQueuedImport(
                "{\"asin\":\"B00ABCDEF1\",\"rootPath\":\"/library\",\"monitored\":true,\"separateBook\":false}", 0, null));

            var drain = await Build().RunDueAsync();

            Assert.Equal(1, drain.Ran);
            Assert.Equal(FoundBookState.Imported, (await _repository.GetAsync(row.Id))!.State);
        }

        private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
        {
            private DateTimeOffset _utcNow = utcNow;

            public override DateTimeOffset GetUtcNow() => _utcNow;

            public void Advance(TimeSpan duration) => _utcNow += duration;
        }
    }
}
