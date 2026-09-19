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
using Listenarr.Infrastructure.FoundBooks.Scanning;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.FoundBooks
{
    /// <summary>
    /// The invariant the whole feature is for: once every found book in a watch folder
    /// has been imported or discarded, the folder holds nothing but what is still
    /// downloading. Run end to end — real files, the real scanner and scan service,
    /// the real decisions and cleanup — over a folder shaped like a real pack, in
    /// several orders, with imports and discards mixed.
    /// </summary>
    [Trait("Name", "FoundBookNoStragglersTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookNoStragglersTests : BaseTests
    {
        private readonly FakeProbe _probe = new();
        private readonly Mock<IHubBroadcaster> _broadcaster = new();
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

        private FoundBookWatchFolder Folder => new(_watch, FileSystemPathSemantics.CurrentHostDefault);

        private FoundBookScanService BuildScanService() => new(
            new FixedResolver(Folder),
            new FoundBookScanner(_fileSystem, _probe, _provider.GetRequiredService<IConfigurationService>(), NullLogger<FoundBookScanner>.Instance),
            _repository,
            _audiobookRepository,
            _downloadRepository,
            new FoundBookCleanup(_fileSystem, _rootFolderRepository, NullLogger<FoundBookCleanup>.Instance),
            _broadcaster.Object,
            new OldClock(),
            NullLogger<FoundBookScanService>.Instance);

        private FoundBookDecisionService BuildDecisions() => new(
            _repository,
            new FoundBookCleanup(_fileSystem, _rootFolderRepository, NullLogger<FoundBookCleanup>.Instance),
            _fileSystem,
            _semantics.Object,
            _historyRepository,
            _scanProcessor.Object,
            TimeProvider.System,
            NullLogger<FoundBookDecisionService>.Instance);

        private async Task Audio(string relative, string album, string artist)
        {
            var path = await Write(relative, "audio");
            _probe.Tags[path] = new FoundBookProbeSnapshot(true, null, 3600, 0, null, null, null, album, artist, null, null, null, null, null, null, null);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));
        }

        private async Task<string> Write(string relative, string content = "x")
        {
            var full = Path.Join(_watch, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, content);
            File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddHours(-1));
            return full;
        }

        /// <summary>A pack: loose files from three books with companions, nested rips, a split book, junk, and one download still in flight.</summary>
        private async Task GivenThePack()
        {
            for (var i = 1; i <= 3; i++) await Audio($"ENCR-ODY3 {i:00}-03.mp3", "Homeworld", "Evan Currie");
            await Write("Evan Currie - Homeworld (2013).jpg");
            await Write("Evan Currie - Homeworld (2013).txt", "Odyssey One 3");
            for (var i = 1; i <= 2; i++) await Audio($"Evan Currie - King of Thieves {i:00}-02.mp3", "King of Thieves", "Evan Currie");
            await Write("Evan Currie - King of Thieves (2014).jpg");
            for (var i = 1; i <= 2; i++) await Audio($"lfl03_Courageous_01 - {i:00}.mp3", "The Lost Fleet - book 03", "Jack Campbell");
            for (var i = 1; i <= 2; i++) await Audio($"Courageous 02/lfl03_Courageous_02 - {i:00}.mp3", "The Lost Fleet - book 03", "Jack Campbell");
            await Write("Courageous 02/lfl03_Courageous - 00.jpg");
            for (var i = 1; i <= 2; i++) await Audio($"522c9976fb072ef9c1e94069/HHHY-MF02 (2009)/Hugh Howey - Molly Fyde (2009)/Hugh Howey - Molly Fyde {i:00}-02.mp3", "Molly Fyde and the Land of Light", "Hugh Howey");
            await Write("522c9976fb072ef9c1e94069/HHHY-MF02 (2009)/Hugh Howey - Molly Fyde (2009)/Hugh Howey - Molly Fyde (2009).txt", "Length: 2 hrs and 0 mins");
            await Write("522c9976fb072ef9c1e94069/HHHY-MF02 (2009)/Hugh Howey - Molly Fyde (2009)/cover.jpg");
            await Write("522c9976fb072ef9c1e94069/.DS_Store");
            await Write(".DS_Store");
            await Write("Thumbs.db");
            await Audio("Still Downloading/book.mp3", "Something", "Someone");
            await Write("Still Downloading/book.mp3.part");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task ImportingOrDiscardingEveryBook_LeavesOnlyTheDownloadInFlight(int seed)
        {
            await GivenThePack();
            await BuildScanService().ScanAllAsync();
            var rows = (await _repository.GetAllAsync()).ToList();
            Assert.Equal(5, rows.Count);
            Assert.Equal(4, rows.Count(r => r.State == FoundBookState.Pending));
            Assert.Single(rows, r => r.State == FoundBookState.Blocked && r.BlockedKind == FoundBookBlockedKind.Downloading);

            var random = new Random(seed);
            var decisions = BuildDecisions();
            foreach (var row in rows.Where(r => r.State == FoundBookState.Pending).OrderBy(_ => random.Next()))
            {
                if (random.Next(2) == 0)
                {
                    // What a person's Add does: the manual import moves the audio out,
                    // then finish-import clears what it left.
                    Assert.True((await decisions.BeginImportAsync(row.Id)).Success);
                    foreach (var file in FoundBookFilesJson.Deserialize(row.FilesJson).Where(f => f.IsAudio))
                    {
                        File.Delete(file.Path);
                    }

                    var finished = await decisions.FinishImportAsync(row.Id, audiobookId: 1);
                    Assert.True(finished.Success, finished.Error);
                    Assert.Empty(finished.Skipped);
                }
                else
                {
                    var discarded = await decisions.DiscardAsync(row.Id);
                    Assert.True(discarded.Success, discarded.Error);
                    Assert.Empty(discarded.Skipped);
                }
            }

            var remaining = Directory.EnumerateFileSystemEntries(_watch, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(_watch, p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            Assert.Equal(
                ["Still Downloading", Path.Join("Still Downloading", "book.mp3"), Path.Join("Still Downloading", "book.mp3.part")],
                remaining);
            Assert.True(Directory.Exists(_watch));

            // And the next scan agrees: nothing offered, the download still waiting.
            await BuildScanService().ScanAllAsync();
            var after = await _repository.GetAllAsync();
            Assert.DoesNotContain(after, r => r.State == FoundBookState.Pending);
            Assert.Single(after, r => r.State == FoundBookState.Blocked);
        }

        private sealed class FixedResolver(FoundBookWatchFolder folder) : IFoundBookWatchFolderResolver
        {
            public Task<FoundBookWatchFolders> ResolveAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new FoundBookWatchFolders([folder], [], [], true));
        }

        /// <summary>A clock far enough ahead of the files' mtimes that every cluster is settled on first sight.</summary>
        private sealed class OldClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddMinutes(30);
        }

        private sealed class FakeProbe : IFoundBookProbe
        {
            public Dictionary<string, FoundBookProbeSnapshot> Tags { get; } = new(StringComparer.Ordinal);

            public Task<FoundBookProbeSnapshot> ProbeAsync(string path, CancellationToken cancellationToken = default) =>
                Task.FromResult(Tags.TryGetValue(path, out var snapshot) ? snapshot : FfprobeFoundBookProbe.Failed("not a real file"));
        }
    }
}
