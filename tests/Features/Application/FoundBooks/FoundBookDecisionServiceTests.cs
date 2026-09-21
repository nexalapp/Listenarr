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
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.FoundBooks
{
    /// <summary>
    /// What the operator's decisions do on disk, and what they refuse to do.
    /// </summary>
    [Trait("Name", "FoundBookDecisionServiceTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookDecisionServiceTests : BaseTests
    {
        private readonly Mock<IFoundBookScanProcessor> _scanProcessor = new();
        private readonly Mock<IFileSystemSemanticsResolver> _semantics = new();
        private IFoundBookRepository _repository = null!;
        private IFileSystem _fileSystem = null!;
        private string _watch = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _repository = _provider.GetRequiredService<IFoundBookRepository>();
            _fileSystem = _provider.GetRequiredService<IFileSystem>();
            _watch = FileService.GetTempDirectory("watch");
            _semantics
                .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<FileSystemCaseSensitivityMode>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, FileSystemCaseSensitivityMode _, CancellationToken _) =>
                    new FileSystemSemanticsResolution(FileSystemPathSemantics.CurrentHostDefault, PathIdentityState.Valid, path));
        }

        private FoundBookDecisionService BuildService() => new(
            _repository,
            new FoundBookCleanup(_fileSystem, _rootFolderRepository, NullLogger<FoundBookCleanup>.Instance),
            _fileSystem,
            _semantics.Object,
            _historyRepository,
            _scanProcessor.Object,
            TimeProvider.System,
            NullLogger<FoundBookDecisionService>.Instance);

        private async Task<string> Write(string relative, string content = "x")
        {
            var full = Path.Join(_watch, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, content);
            return full;
        }

        private static FoundBookFileEntry Entry(string path, bool audio) =>
            new(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), audio, null);

        /// <summary>A row whose files are the given paths, exactly as a scan would have recorded them.</summary>
        private async Task<FoundBook> Row(
            string bookFolder,
            IEnumerable<(string Path, bool Audio)> files,
            FoundBookState state = FoundBookState.Pending,
            FoundBookBlockedKind blockedKind = FoundBookBlockedKind.None)
        {
            var entries = files.Select(f => Entry(f.Path, f.Audio)).ToList();
            return await _repository.AddAsync(new FoundBook
            {
                ClusterKey = Guid.NewGuid().ToString("N"),
                Signature = "sig",
                WatchFolder = _watch,
                BookFolder = bookFolder,
                FilesJson = FoundBookFilesJson.Serialize(entries),
                AudioFileCount = entries.Count(e => e.IsAudio),
                DetectedTitle = "Wool",
                State = state,
                BlockedKind = blockedKind
            });
        }

        [Fact]
        public async Task Discard_DeletesTheFilesAndTheEmptyFoldersTheyLeave_ButNotTheWatchFolder()
        {
            var pack = Path.Join(_watch, "5da8", "HHHY-MF02 (2009)", "Hugh Howey - Molly Fyde (2009)");
            var a = await Write("5da8/HHHY-MF02 (2009)/Hugh Howey - Molly Fyde (2009)/01.mp3");
            var b = await Write("5da8/HHHY-MF02 (2009)/Hugh Howey - Molly Fyde (2009)/02.mp3");
            var nfo = await Write("5da8/HHHY-MF02 (2009)/Hugh Howey - Molly Fyde (2009)/book.nfo");
            await Write("5da8/.DS_Store");
            var row = await Row(pack, [(a, true), (b, true), (nfo, false)]);

            var result = await BuildService().DiscardAsync(row.Id);

            Assert.True(result.Success, result.Error);
            Assert.False(File.Exists(a));
            Assert.False(Directory.Exists(Path.Join(_watch, "5da8")));
            Assert.True(Directory.Exists(_watch));
            Assert.Equal(FoundBookState.Discarded, (await _repository.GetAsync(row.Id))!.State);
            _scanProcessor.Verify(p => p.TriggerScan(), Times.Once);

            var history = await _historyRepository.QueryAsync(new HistoryQuery { Limit = 10 });
            Assert.Contains(history.Records, h => h.EventType == "FileDeleted" && h.Source == FoundBookDecisionService.HistorySource);
        }

        [Fact]
        public async Task Discard_LeavesAFolderThatStillHoldsSomethingElse()
        {
            var a = await Write("Pack/book-a.mp3");
            var other = await Write("Pack/book-b.mp3");
            var row = await Row(Path.Join(_watch, "Pack"), [(a, true)]);

            var result = await BuildService().DiscardAsync(row.Id);

            Assert.True(result.Success, result.Error);
            Assert.False(File.Exists(a));
            Assert.True(File.Exists(other));
            Assert.True(Directory.Exists(Path.Join(_watch, "Pack")));
        }

        [Fact]
        public async Task Discard_SkipsAFileThatChangedSinceTheScan()
        {
            var a = await Write("Book/01.mp3");
            var row = await Row(Path.Join(_watch, "Book"), [(a, true)]);
            await File.WriteAllTextAsync(a, "grew since the operator looked");
            File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(5));

            var result = await BuildService().DiscardAsync(row.Id);

            Assert.True(result.Success);
            Assert.True(File.Exists(a));
            Assert.Contains(result.Skipped, s => s.Contains("changed since"));
        }

        [Fact]
        public async Task Discard_NeverTouchesAFileOutsideTheWatchFolder()
        {
            var elsewhere = FileService.GetTempDirectory("elsewhere");
            var outside = Path.Join(elsewhere, "precious.mp3");
            await File.WriteAllTextAsync(outside, "x");
            var row = await Row(Path.Join(_watch, "Book"), [(outside, true)]);

            var result = await BuildService().DiscardAsync(row.Id);

            Assert.True(File.Exists(outside));
            Assert.Single(result.Skipped);
        }

        [Fact]
        public async Task Discard_RefusesFilesADownloadStillOwns()
        {
            var a = await Write("Book/01.mp3");
            var row = await Row(Path.Join(_watch, "Book"), [(a, true)], FoundBookState.Blocked, FoundBookBlockedKind.OwnedByDownload);

            var result = await BuildService().DiscardAsync(row.Id);

            Assert.Equal(FoundBookDecisionFailure.WrongState, result.Failure);
            Assert.True(File.Exists(a));
        }

        [Fact]
        public async Task FinishImport_RefusesWhileAudioRemains_AndReturnsTheRowToPending()
        {
            var a = await Write("Book/01.mp3");
            var row = await Row(Path.Join(_watch, "Book"), [(a, true)], FoundBookState.Importing);

            var result = await BuildService().FinishImportAsync(row.Id, audiobookId: 7);

            Assert.Equal(FoundBookDecisionFailure.FilesRemain, result.Failure);
            Assert.Equal(FoundBookState.Pending, (await _repository.GetAsync(row.Id))!.State);
            Assert.True(File.Exists(a));
        }

        [Fact]
        public async Task FinishImport_ClearsLeftoversAndRecordsTheImport()
        {
            var a = await Write("Book/01.mp3");
            var nfo = await Write("Book/book.nfo");
            var m3u = await Write("Book/book.m3u");
            var row = await Row(Path.Join(_watch, "Book"), [(a, true), (nfo, false), (m3u, false)], FoundBookState.Importing);
            File.Delete(a); // the manual import moved it

            var result = await BuildService().FinishImportAsync(row.Id, audiobookId: 7);

            Assert.True(result.Success, result.Error);
            Assert.False(File.Exists(nfo));
            Assert.False(Directory.Exists(Path.Join(_watch, "Book")));
            var after = (await _repository.GetAsync(row.Id))!;
            Assert.Equal(FoundBookState.Imported, after.State);
            Assert.Equal(7, after.MatchedAudiobookId);
            var history = await _historyRepository.QueryAsync(new HistoryQuery { Limit = 10 });
            Assert.Contains(history.Records, h => h.EventType == "Imported" && h.AudiobookId == 7);
        }

        [Fact]
        public async Task IgnoreAndRestore_RoundTrip()
        {
            var a = await Write("Book/01.mp3");
            var row = await Row(Path.Join(_watch, "Book"), [(a, true)]);
            var service = BuildService();

            Assert.True((await service.IgnoreAsync(row.Id)).Success);
            Assert.Equal(FoundBookState.Ignored, (await _repository.GetAsync(row.Id))!.State);

            Assert.True((await service.RestoreAsync(row.Id)).Success);
            var restored = (await _repository.GetAsync(row.Id))!;
            Assert.Equal(FoundBookState.Blocked, restored.State);
            Assert.Equal(FoundBookBlockedKind.Settling, restored.BlockedKind);
        }

        [Fact]
        public async Task BeginImport_OnlyFromPending()
        {
            var a = await Write("Book/01.mp3");
            var row = await Row(Path.Join(_watch, "Book"), [(a, true)], FoundBookState.Blocked, FoundBookBlockedKind.Settling);

            var result = await BuildService().BeginImportAsync(row.Id);

            Assert.Equal(FoundBookDecisionFailure.WrongState, result.Failure);
        }

        [Fact]
        public async Task RecoverStrandedImports_PutsBackOnlyTheRowsLeftImportingWithNoQueuedRequest()
        {
            var a = await Write("A/01.mp3");
            var b = await Write("B/01.mp3");
            var c = await Write("C/01.mp3");
            var d = await Write("D/01.mp3");
            var stranded = await Row(Path.Join(_watch, "A"), [(a, true)], state: FoundBookState.Importing);
            var pending = await Row(Path.Join(_watch, "B"), [(b, true)]);
            var ignored = await Row(Path.Join(_watch, "C"), [(c, true)], state: FoundBookState.Ignored);
            var queued = await Row(Path.Join(_watch, "D"), [(d, true)], state: FoundBookState.Importing);
            await _repository.UpdateAsync(queued.Id, r => r.ImportRequestJson = "{}");

            var reset = await BuildService().RecoverStrandedImportsAsync(TimeSpan.Zero);

            Assert.Equal([stranded.Id], reset);
            var recovered = (await _repository.GetAsync(stranded.Id))!;
            Assert.Equal(FoundBookState.Pending, recovered.State);
            Assert.Contains("interrupted", recovered.LastImportError);
            Assert.Equal(FoundBookState.Pending, (await _repository.GetAsync(pending.Id))!.State);
            Assert.Equal(FoundBookState.Ignored, (await _repository.GetAsync(ignored.Id))!.State);
            // The worker resumes a queued request; recovery must not take it away.
            Assert.Equal(FoundBookState.Importing, (await _repository.GetAsync(queued.Id))!.State);
        }

        [Fact]
        public async Task RecoverStrandedImports_LeavesAnImportYoungerThanTheAge()
        {
            var a = await Write("A/01.mp3");
            var running = await Row(Path.Join(_watch, "A"), [(a, true)], state: FoundBookState.Importing);
            await _repository.UpdateAsync(running.Id, r => r.ImportStartedAt = DateTime.UtcNow.AddMinutes(-5));

            var reset = await BuildService().RecoverStrandedImportsAsync(TimeSpan.FromHours(1));

            Assert.Empty(reset);
            Assert.Equal(FoundBookState.Importing, (await _repository.GetAsync(running.Id))!.State);
        }

        [Fact]
        public async Task BeginAbortAndFinish_KeepTheImportFieldsInStep()
        {
            var a = await Write("A/01.mp3");
            var row = await Row(Path.Join(_watch, "A"), [(a, true)]);
            var service = BuildService();

            var begun = await service.BeginImportAsync(row.Id, new FoundBookQueuedImport("{\"asin\":\"X\"}", 2, DateTime.UtcNow.AddMinutes(1)));
            Assert.True(begun.Success, begun.Error);
            Assert.NotNull(begun.Book!.ImportStartedAt);
            Assert.Equal("{\"asin\":\"X\"}", begun.Book.ImportRequestJson);
            Assert.Equal(2, begun.Book.ImportAttempts);
            Assert.NotNull(begun.Book.ImportNotBefore);

            var aborted = await service.AbortImportAsync(row.Id, "disk full");
            Assert.True(aborted.Success, aborted.Error);
            Assert.Equal(FoundBookState.Pending, aborted.Book!.State);
            Assert.Null(aborted.Book.ImportStartedAt);
            Assert.Null(aborted.Book.ImportRequestJson);
            Assert.Null(aborted.Book.ImportNotBefore);
            Assert.Equal(0, aborted.Book.ImportAttempts);
            Assert.Equal("disk full", aborted.Book.LastImportError);

            // A fresh begin clears the old reason; a retry (attempt > 0) keeps it so
            // the row can say what it is retrying after.
            var retrying = await service.BeginImportAsync(row.Id, new FoundBookQueuedImport("{}", 1, null));
            Assert.Equal("disk full", retrying.Book!.LastImportError);
            await service.AbortImportAsync(row.Id, "disk full");
            var fresh = await service.BeginImportAsync(row.Id);
            Assert.Null(fresh.Book!.LastImportError);
            await service.AbortImportAsync(row.Id, "disk full");

            // A begin, then a finish with the file gone, clears the error too.
            await service.BeginImportAsync(row.Id);
            File.Delete(a);
            var finished = await service.FinishImportAsync(row.Id, 42);
            Assert.True(finished.Success, finished.Error);
            Assert.Null(finished.Book!.LastImportError);
            Assert.Null(finished.Book.ImportStartedAt);
        }
    }
}
