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
using Listenarr.Domain.FoundBooks;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.FoundBooks
{
    /// <summary>
    /// What the automatic add does without a person: only the certain cases, and every
    /// failure leaves the row where a person can see it.
    /// </summary>
    [Trait("Name", "FoundBookAutoAddServiceTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookAutoAddServiceTests : BaseTests
    {
        private readonly Mock<IFoundBookCatalogueMatcher> _matcher = new();
        private readonly Mock<ILibraryAddService> _libraryAdd = new();
        private readonly Mock<IFoundBookDecisionService> _decisions = new();
        private readonly FakeImporter _importer = new();
        private IFoundBookRepository _repository = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _repository = _provider.GetRequiredService<IFoundBookRepository>();
            await _rootFolderRepository.AddAsync(new RootFolderBuilder().WithPath("/library").WithIsDefault().Build());
            _decisions.Setup(d => d.BeginImportAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, CancellationToken _) => FoundBookDecisionResult.Ok(new FoundBook { Id = id, State = FoundBookState.Importing }));
            _decisions.Setup(d => d.AbortImportAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, CancellationToken _) => FoundBookDecisionResult.Ok(new FoundBook { Id = id }));
            _decisions.Setup(d => d.FinishImportAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, int _, bool _, CancellationToken _) => FoundBookDecisionResult.Ok(new FoundBook { Id = id, State = FoundBookState.Imported }));
            _libraryAdd.Setup(l => l.AddToLibraryAsync(It.IsAny<LibraryAddOperationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LibraryAddOperationResult { Added = true, Audiobook = new Audiobook { Id = 42, Title = "Wool" } });
        }

        private async Task GivenAutoAdd(bool on)
        {
            var settings = await _applicationSettingsRepository.GetAsync() ?? new ApplicationSettings();
            settings.FoundBooksAutoAdd = on;
            await _applicationSettingsRepository.SaveAsync(settings);
        }

        private async Task<FoundBook> GivenRow(
            string folder = "/downloads/Wool",
            FoundBookCompleteness completeness = FoundBookCompleteness.Complete,
            FoundBookLibraryStatus library = FoundBookLibraryStatus.New,
            FoundBookState state = FoundBookState.Pending) =>
            await _repository.AddAsync(new FoundBook
            {
                ClusterKey = Guid.NewGuid().ToString("N"),
                Signature = "sig",
                WatchFolder = "/downloads",
                BookFolder = folder,
                FilesJson = FoundBookFilesJson.Serialize([new FoundBookFileEntry($"{folder}/01.mp3", 1, DateTime.UtcNow, true, null)]),
                AudioFileCount = 1,
                DetectedTitle = "Wool",
                DetectedAuthor = "Hugh Howey",
                Completeness = completeness,
                LibraryStatus = library,
                State = state
            });

        private void GivenMatch(bool high) =>
            _matcher.Setup(m => m.MatchAsync(It.IsAny<FoundBook>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FoundBookCatalogueMatch(new AudibleBookMetadata { Asin = "B00ABCDEF1", Title = "Wool" }, high, high ? "exact" : "nearest"));

        private FoundBookAutoAddService BuildService() => new(
            _repository,
            _provider.GetRequiredService<IConfigurationService>(),
            _matcher.Object,
            _libraryAdd.Object,
            _importer,
            _decisions.Object,
            _rootFolderRepository,
            NullLogger<FoundBookAutoAddService>.Instance);

        [Fact]
        public async Task Off_DoesNothing()
        {
            await GivenAutoAdd(false);
            await GivenRow();
            GivenMatch(high: true);

            var summary = await BuildService().RunAsync();

            Assert.Equal(0, summary.Considered);
            _matcher.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CertainMatch_IsAddedImportedAndFinishedAsAuto()
        {
            await GivenAutoAdd(true);
            var row = await GivenRow();
            GivenMatch(high: true);

            var summary = await BuildService().RunAsync();

            Assert.Equal(1, summary.Added);
            _decisions.Verify(d => d.BeginImportAsync(row.Id, It.IsAny<CancellationToken>()), Times.Once);
            _libraryAdd.Verify(l => l.AddToLibraryAsync(
                It.Is<LibraryAddOperationRequest>(r => r.Metadata.Asin == "B00ABCDEF1" && r.DestinationPath == "/library" && r.HistorySource == "FoundBooks"),
                It.IsAny<CancellationToken>()), Times.Once);
            var import = Assert.Single(_importer.Calls);
            Assert.Equal((row.Id, 42, true), import);
            _decisions.Verify(d => d.FinishImportAsync(row.Id, 42, true, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UncertainMatch_IsLeftForReview()
        {
            await GivenAutoAdd(true);
            var row = await GivenRow();
            GivenMatch(high: false);

            var summary = await BuildService().RunAsync();

            Assert.Equal(1, summary.Skipped);
            Assert.Contains(summary.Notes, n => n.Contains("left for review"));
            _decisions.Verify(d => d.BeginImportAsync(row.Id, It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(_importer.Calls);
        }

        [Fact]
        public async Task OnlyOfferedCompleteNewRows_AreConsidered()
        {
            await GivenAutoAdd(true);
            await GivenRow(completeness: FoundBookCompleteness.Unknown);
            await GivenRow(folder: "/downloads/B", library: FoundBookLibraryStatus.InLibrary);
            await GivenRow(folder: "/downloads/C", state: FoundBookState.Ignored);
            GivenMatch(high: true);

            var summary = await BuildService().RunAsync();

            Assert.Equal(0, summary.Considered);
        }

        [Fact]
        public async Task ImportFailure_AbortsAndLeavesTheRow()
        {
            await GivenAutoAdd(true);
            var row = await GivenRow();
            GivenMatch(high: true);
            _importer.Fail = "disk full";

            var summary = await BuildService().RunAsync();

            Assert.Equal(1, summary.Skipped);
            _decisions.Verify(d => d.AbortImportAsync(row.Id, It.IsAny<CancellationToken>()), Times.Once);
            _decisions.Verify(d => d.FinishImportAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Contains(summary.Notes, n => n.Contains("disk full"));
        }

        [Fact]
        public async Task EditionAlreadyHeldWithAFile_IsLeftForReview()
        {
            await GivenAutoAdd(true);
            var row = await GivenRow();
            GivenMatch(high: true);
            _libraryAdd.Setup(l => l.AddToLibraryAsync(It.IsAny<LibraryAddOperationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LibraryAddOperationResult { AlreadyExists = true, Audiobook = new Audiobook { Id = 7, FilePath = "/library/wool.m4b" } });

            var summary = await BuildService().RunAsync();

            Assert.Equal(1, summary.Skipped);
            _decisions.Verify(d => d.AbortImportAsync(row.Id, It.IsAny<CancellationToken>()), Times.Once);
            Assert.Empty(_importer.Calls);
        }

        [Fact]
        public async Task SharedFolder_ImportsWithoutCompanions()
        {
            await GivenAutoAdd(true);
            var a = await GivenRow(folder: "/downloads/pack");
            await GivenRow(folder: "/downloads/pack");
            GivenMatch(high: true);

            await BuildService().RunAsync();

            Assert.All(_importer.Calls, call => Assert.False(call.IncludeCompanions));
            Assert.Contains(_importer.Calls, call => call.Id == a.Id);
        }

        private sealed class FakeImporter : IFoundBookImporter
        {
            public List<(int Id, int AudiobookId, bool IncludeCompanions)> Calls { get; } = [];
            public string? Fail { get; set; }
            public bool IsAvailable => true;

            public Task<FoundBookImportOutcome> ImportAsync(FoundBook row, int audiobookId, bool includeCompanions, CancellationToken cancellationToken = default)
            {
                Calls.Add((row.Id, audiobookId, includeCompanions));
                return Task.FromResult(Fail == null
                    ? new FoundBookImportOutcome(true, 1, 1, null)
                    : new FoundBookImportOutcome(false, 0, 1, Fail));
            }
        }
    }
}
