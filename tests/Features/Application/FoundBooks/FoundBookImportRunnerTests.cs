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
using System.Data.Common;
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Domain.FoundBooks;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.FoundBooks
{
    /// <summary>
    /// The one import sequence, and above all what it does when a step fails: the row
    /// is put back by the same process, whatever failed and however it failed.
    /// </summary>
    [Trait("Name", "FoundBookImportRunnerTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookImportRunnerTests : BaseTests
    {
        private readonly Mock<ILibraryAddService> _libraryAdd = new();
        private readonly Mock<IFoundBookDecisionService> _decisions = new();
        private readonly Mock<ILibraryDestinationPlanner> _planner = new();
        private readonly FakeImporter _importer = new();
        private readonly List<string> _states = [];
        private readonly List<string?> _abortErrors = [];

        private static readonly FoundBook Row = new()
        {
            Id = 5,
            WatchFolder = "/downloads",
            BookFolder = "/downloads/Wool",
            FilesJson = FoundBookFilesJson.Serialize([new FoundBookFileEntry("/downloads/Wool/01.mp3", 1, DateTime.UtcNow, true, null)]),
            AudioFileCount = 1,
            DetectedTitle = "Wool",
            State = FoundBookState.Pending
        };

        private static readonly AudibleBookMetadata Metadata = new() { Asin = "B00ABCDEF1", Title = "Wool" };

        private static FoundBookImportOptions Manual(bool separate = false) =>
            new("/library", Monitored: true, AllowDuplicateEdition: separate, IncludeCompanions: true, AutoAdded: false);

        private static FoundBookImportOptions Auto() =>
            new("/library", Monitored: true, AllowDuplicateEdition: false, IncludeCompanions: true, AutoAdded: true);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _decisions.Setup(d => d.BeginImportAsync(Row.Id, null, It.IsAny<CancellationToken>()))
                .Callback(() => _states.Add("begin"))
                .ReturnsAsync(FoundBookDecisionResult.Ok(new FoundBook { Id = Row.Id, State = FoundBookState.Importing }));
            _decisions.Setup(d => d.AbortImportAsync(Row.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback((int _, string? error, CancellationToken _) => { _states.Add("abort"); _abortErrors.Add(error); })
                .ReturnsAsync(FoundBookDecisionResult.Ok(new FoundBook { Id = Row.Id, State = FoundBookState.Pending }));
            _decisions.Setup(d => d.FinishImportAsync(Row.Id, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback(() => _states.Add("finish"))
                .ReturnsAsync(FoundBookDecisionResult.Ok(new FoundBook { Id = Row.Id, State = FoundBookState.Imported }, ["cover.jpg"]));
            _planner.Setup(p => p.PlanBookFolderAsync(It.IsAny<AudibleBookMetadata>(), "/library", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LibraryDestinationPlan("/library/Hugh Howey/Wool", "Hugh Howey/Wool"));
            GivenAdd(new LibraryAddOperationResult { Added = true, Audiobook = new Audiobook { Id = 42, Title = "Wool" } });
        }

        private void GivenAdd(LibraryAddOperationResult result) =>
            _libraryAdd.Setup(l => l.AddToLibraryAsync(It.IsAny<LibraryAddOperationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);

        private FoundBookImportRunner Build() => new(
            _libraryAdd.Object,
            _planner.Object,
            _importer,
            _decisions.Object,
            NullLogger<FoundBookImportRunner>.Instance);

        [Fact]
        public async Task HappyPath_AddsIntoThePlannedFolder_ImportsAndFinishes()
        {
            var result = await Build().RunAsync(Row, Metadata, Manual());

            Assert.True(result.Success, result.Error);
            Assert.Equal(42, result.AudiobookId);
            Assert.Equal(FoundBookState.Imported, result.Book!.State);
            Assert.Equal(["cover.jpg"], result.Skipped);
            Assert.Equal(["begin", "finish"], _states);
            _libraryAdd.Verify(l => l.AddToLibraryAsync(
                It.Is<LibraryAddOperationRequest>(r =>
                    r.Metadata.Asin == "B00ABCDEF1"
                    && r.DestinationPath == "/library/Hugh Howey/Wool"
                    && r.HistorySource == "FoundBooks"
                    && !r.AllowDuplicateEdition),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal([(Row.Id, 42, true)], _importer.Calls);
        }

        [Fact]
        public async Task SeparateBook_AddsADuplicateEdition()
        {
            await Build().RunAsync(Row, Metadata, Manual(separate: true));

            _libraryAdd.Verify(l => l.AddToLibraryAsync(
                It.Is<LibraryAddOperationRequest>(r => r.AllowDuplicateEdition), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RowNotOffered_IsRefusedBeforeAnythingIsAdded()
        {
            _decisions.Setup(d => d.BeginImportAsync(Row.Id, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(FoundBookDecisionResult.Fail(FoundBookDecisionFailure.WrongState, "The book is Ignored."));

            var result = await Build().RunAsync(Row, Metadata, Manual());

            Assert.Equal(FoundBookImportFailure.WrongState, result.Failure);
            _libraryAdd.VerifyNoOtherCalls();
            Assert.Empty(_importer.Calls);
        }

        [Fact]
        public async Task AddRefused_PutsTheRowBack()
        {
            GivenAdd(new LibraryAddOperationResult { Added = false, ValidationMessage = "DestinationPath is invalid" });

            var result = await Build().RunAsync(Row, Metadata, Manual());

            Assert.Equal(FoundBookImportFailure.AddRefused, result.Failure);
            Assert.Contains("DestinationPath is invalid", result.Error);
            Assert.Equal(["begin", "abort"], _states);
            Assert.Equal([result.Error], _abortErrors);
            Assert.Equal(FoundBookState.Pending, result.Book!.State);
            Assert.Empty(_importer.Calls);
        }

        [Fact]
        public async Task ImportFailed_PutsTheRowBack()
        {
            _importer.Fail = "disk full";

            var result = await Build().RunAsync(Row, Metadata, Manual());

            Assert.Equal(FoundBookImportFailure.ImportFailed, result.Failure);
            Assert.Equal("disk full", result.Error);
            Assert.Equal(["begin", "abort"], _states);
        }

        [Fact]
        public async Task AStepThrowing_PutsTheRowBack_AndNamesTheDatabaseWhenItWasTheDatabase()
        {
            _importer.Throw = new FakeDbException("SQLite Error 10: 'disk I/O error'.");

            var result = await Build().RunAsync(Row, Metadata, Manual());

            Assert.Equal(FoundBookImportFailure.Persistence, result.Failure);
            Assert.Contains("database was unavailable", result.Error);
            Assert.Contains("nothing was imported", result.Error);
            Assert.Equal(["begin", "abort"], _states);
            Assert.Equal([result.Error], _abortErrors);
            Assert.Equal(FoundBookState.Pending, result.Book!.State);
        }

        [Fact]
        public async Task AStepThrowingSomethingElse_PutsTheRowBack_WithThatReason()
        {
            _importer.Throw = new InvalidOperationException("the watch folder vanished");

            var result = await Build().RunAsync(Row, Metadata, Manual());

            Assert.Equal(FoundBookImportFailure.ImportFailed, result.Failure);
            Assert.Equal("the watch folder vanished", result.Error);
            Assert.Equal(["begin", "abort"], _states);
        }

        [Fact]
        public async Task AbortFailingToo_IsRetried_SoAnOutageShorterThanTheRetriesStillPutsTheRowBack()
        {
            _importer.Fail = "disk full";
            var attempts = 0;
            _decisions.Setup(d => d.AbortImportAsync(Row.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    attempts++;
                    if (attempts < 3) throw new FakeDbException("disk I/O error");
                    return Task.FromResult(FoundBookDecisionResult.Ok(new FoundBook { Id = Row.Id, State = FoundBookState.Pending }));
                });

            var result = await Build().RunAsync(Row, Metadata, Manual());

            Assert.Equal(3, attempts);
            Assert.Equal(FoundBookState.Pending, result.Book!.State);
        }

        [Fact]
        public async Task ExistingRecord_IsReusedByAPersonsAdd_SoARetryAfterAPartialFailureGoesOn()
        {
            // A previous attempt added the record and stopped: the record exists with no
            // file. The retry must attach the file to it rather than refuse.
            GivenAdd(new LibraryAddOperationResult { AlreadyExists = true, Audiobook = new Audiobook { Id = 7, Title = "Wool" } });

            var result = await Build().RunAsync(Row, Metadata, Manual());

            Assert.True(result.Success, result.Error);
            Assert.Equal(7, result.AudiobookId);
            Assert.Equal([(Row.Id, 7, true)], _importer.Calls);
        }

        [Fact]
        public async Task ExistingRecordWithAFile_JoinsItForAPerson_ButIsLeftForReviewByTheScanner()
        {
            GivenAdd(new LibraryAddOperationResult { AlreadyExists = true, Audiobook = new Audiobook { Id = 7, FilePath = "/library/wool.m4b" } });

            var manual = await Build().RunAsync(Row, Metadata, Manual());
            Assert.True(manual.Success, manual.Error);

            _states.Clear();
            _importer.Calls.Clear();
            var auto = await Build().RunAsync(Row, Metadata, Auto());

            Assert.Equal(FoundBookImportFailure.AddRefused, auto.Failure);
            Assert.Contains("left for review", auto.Error);
            Assert.Equal(["begin", "abort"], _states);
            Assert.Empty(_importer.Calls);
        }

        [Fact]
        public async Task FinishRefusingBecauseFilesRemain_LeavesTheRowWhereFinishPutIt()
        {
            _decisions.Setup(d => d.FinishImportAsync(Row.Id, 42, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(FoundBookDecisionResult.Fail(FoundBookDecisionFailure.FilesRemain, "1 audio file(s) are still in the watch folder."));

            var result = await Build().RunAsync(Row, Metadata, Manual());

            Assert.Equal(FoundBookImportFailure.FinishFailed, result.Failure);
            _decisions.Verify(d => d.AbortImportAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AQueuedRow_IsAlreadyImporting_AndIsNotMarkedAgain()
        {
            var queued = new FoundBook { Id = Row.Id, BookFolder = Row.BookFolder, FilesJson = Row.FilesJson, State = FoundBookState.Importing, ImportRequestJson = "{}" };

            var result = await Build().RunAsync(queued, Metadata, Manual());

            Assert.True(result.Success, result.Error);
            Assert.Equal(["finish"], _states);
        }

        [Fact]
        public async Task NoImporter_RefusesBeforeTouchingTheRow()
        {
            var runner = new FoundBookImportRunner(
                _libraryAdd.Object, _planner.Object, new UnavailableFoundBookImporter(), _decisions.Object, NullLogger<FoundBookImportRunner>.Instance);

            var result = await runner.RunAsync(Row, Metadata, Manual());

            Assert.Equal(FoundBookImportFailure.Unavailable, result.Failure);
            Assert.Empty(_states);
        }

        private sealed class FakeImporter : IFoundBookImporter
        {
            public List<(int Id, int AudiobookId, bool IncludeCompanions)> Calls { get; } = [];
            public string? Fail { get; set; }
            public Exception? Throw { get; set; }
            public bool IsAvailable => true;

            public Task<FoundBookImportOutcome> ImportAsync(FoundBook row, int audiobookId, bool includeCompanions, CancellationToken cancellationToken = default)
            {
                if (Throw != null) throw Throw;
                Calls.Add((row.Id, audiobookId, includeCompanions));
                return Task.FromResult(Fail == null
                    ? new FoundBookImportOutcome(true, 1, 1, null)
                    : new FoundBookImportOutcome(false, 0, 1, Fail));
            }
        }

        /// <summary>What the provider throws, without a provider: any <see cref="DbException"/>.</summary>
        private sealed class FakeDbException(string message) : DbException(message);
    }
}
