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
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.FoundBooks
{
    /// <summary>
    /// A person's Add: which catalogue record and which root the import runs against,
    /// and the refusals that come before the row is touched at all.
    /// </summary>
    [Trait("Name", "FoundBookImportServiceTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookImportServiceTests : BaseTests
    {
        private readonly Mock<ISearchService> _search = new();
        private readonly Mock<IMetadataStrategy> _strategy = new();
        private readonly Mock<IFoundBookImportRunner> _runner = new();
        private IFoundBookRepository _repository = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _repository = _provider.GetRequiredService<IFoundBookRepository>();
            var source = new ApiConfiguration { Name = "Audible", BaseUrl = "https://api.audible.com" };
            _search.Setup(s => s.GetEnabledMetadataSourcesAsync()).ReturnsAsync([source]);
            _strategy.Setup(s => s.CanHandle(source)).Returns(true);
            _strategy.Setup(s => s.FetchMetadataAsync("B00ABCDEF1", source, null, It.IsAny<string?>()))
                .ReturnsAsync(new AudibleBookMetadata { Asin = "B00ABCDEF1", Title = "Wool" });
            _runner.Setup(r => r.RunAsync(It.IsAny<FoundBook>(), It.IsAny<AudibleBookMetadata>(), It.IsAny<FoundBookImportOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FoundBook row, AudibleBookMetadata _, FoundBookImportOptions _, CancellationToken _) =>
                    FoundBookImportResult.Ok(42, row));
        }

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

        private FoundBookImportService Build() => new(
            _repository,
            _rootFolderRepository,
            _search.Object,
            new MetadataStrategyCoordinator([_strategy.Object], NullLogger<MetadataStrategyCoordinator>.Instance),
            _provider.GetRequiredService<IConfigurationService>(),
            _runner.Object);

        [Fact]
        public async Task UsesTheRememberedMatchAndTheDefaultRoot_WhenTheRequestNamesNeither()
        {
            await _rootFolderRepository.AddAsync(new RootFolderBuilder().WithPath("/library").WithIsDefault().Build());
            var row = await GivenRow();

            var result = await Build().ImportAsync(row.Id, new FoundBookManualImportRequest(null, null, Monitored: true, SeparateBook: false));

            Assert.True(result.Success, result.Error);
            _runner.Verify(r => r.RunAsync(
                It.Is<FoundBook>(b => b.Id == row.Id),
                It.Is<AudibleBookMetadata>(m => m.Asin == "B00ABCDEF1"),
                It.Is<FoundBookImportOptions>(o => o.RootPath == "/library" && o.Monitored && !o.AllowDuplicateEdition && o.IncludeCompanions && !o.AutoAdded),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task TheRequestsOwnAsinAndRootWin()
        {
            await _rootFolderRepository.AddAsync(new RootFolderBuilder().WithPath("/library").WithIsDefault().Build());
            var row = await GivenRow(matchAsin: "B00OLDMATCH");

            await Build().ImportAsync(row.Id, new FoundBookManualImportRequest("B00ABCDEF1", "/other", Monitored: false, SeparateBook: true));

            _runner.Verify(r => r.RunAsync(
                It.IsAny<FoundBook>(),
                It.Is<AudibleBookMetadata>(m => m.Asin == "B00ABCDEF1"),
                It.Is<FoundBookImportOptions>(o => o.RootPath == "/other" && !o.Monitored && o.AllowDuplicateEdition),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task NoMatchAnywhere_IsRefusedBeforeTheRowIsTouched()
        {
            await _rootFolderRepository.AddAsync(new RootFolderBuilder().WithPath("/library").WithIsDefault().Build());
            var row = await GivenRow(matchAsin: null);

            var result = await Build().ImportAsync(row.Id, new FoundBookManualImportRequest(null, null, true, false));

            Assert.Equal(FoundBookImportFailure.NoMatch, result.Failure);
            Assert.Equal(FoundBookState.Pending, result.Book!.State);
            _runner.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AnAsinTheCatalogueDoesNotKnow_IsRefused()
        {
            await _rootFolderRepository.AddAsync(new RootFolderBuilder().WithPath("/library").WithIsDefault().Build());
            var row = await GivenRow(matchAsin: "B00UNKNOWN1");

            var result = await Build().ImportAsync(row.Id, new FoundBookManualImportRequest(null, null, true, false));

            Assert.Equal(FoundBookImportFailure.NoMatch, result.Failure);
            Assert.Contains("B00UNKNOWN1", result.Error);
            _runner.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task NoRootFolder_IsRefused()
        {
            var row = await GivenRow();

            var result = await Build().ImportAsync(row.Id, new FoundBookManualImportRequest(null, null, true, false));

            Assert.Equal(FoundBookImportFailure.AddRefused, result.Failure);
            _runner.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ARowSharingItsFolder_ImportsWithoutCompanions()
        {
            await _rootFolderRepository.AddAsync(new RootFolderBuilder().WithPath("/library").WithIsDefault().Build());
            var row = await GivenRow(folder: "/downloads/pack");
            await GivenRow(folder: "/downloads/pack");

            await Build().ImportAsync(row.Id, new FoundBookManualImportRequest(null, null, true, false));

            _runner.Verify(r => r.RunAsync(
                It.IsAny<FoundBook>(), It.IsAny<AudibleBookMetadata>(),
                It.Is<FoundBookImportOptions>(o => !o.IncludeCompanions),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UnknownRow_IsNotFound()
        {
            var result = await Build().ImportAsync(999, new FoundBookManualImportRequest(null, null, true, false));

            Assert.Equal(FoundBookImportFailure.NotFound, result.Failure);
        }
    }
}
