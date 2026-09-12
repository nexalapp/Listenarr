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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Tagging
{
    /// <summary>
    /// The library-wide tag table.
    ///
    /// What is worth pinning here is the difference between the table and the rest of the
    /// tagging feature: it reports what the files actually carry, it lists MP3 books it
    /// cannot write to, and it re-reads a file only when the file has changed. Each of
    /// those is a decision that a later refactor could quietly reverse without breaking
    /// anything else.
    /// </summary>
    [Trait("Name", "LibraryTagIndexServiceTests")]
    [Trait("Category", "Tagging")]
    public sealed class LibraryTagIndexServiceTests : BaseTests, IDisposable
    {
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IAudiobookTagWriter> _writer = new();
        private readonly Mock<IFileSystem> _fileSystem = new();
        private readonly Mock<IRootFolderService> _rootFolders = new();
        private readonly Mock<IRenameService> _rename = new();
        private readonly LibraryTagCache _cache = new();

        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "listenarr-tagindex-" + Guid.NewGuid().ToString("N"));

        private LibraryTagIndexService BuildService() => new(
            _audiobooks.Object,
            _configuration.Object,
            _writer.Object,
            new AudiobookTagPlanner(new FileNamingService(
                _configuration.Object,
                NullLogger<FileNamingService>.Instance)),
            _fileSystem.Object,
            _cache,
            _rootFolders.Object,
            NullLogger<LibraryTagIndexService>.Instance,
            _rename.Object);

        private Audiobook GivenLibrary(params string[] fileNames)
        {
            Directory.CreateDirectory(_directory);

            var audiobook = new AudiobookBuilder()
                .WithId(7)
                .WithTitle("Drive")
                .WithBasePath(_directory)
                .Build();

            audiobook.Authors = ["James S. A. Corey"];
            audiobook.Narrators = ["Jefferson Mays"];
            audiobook.Description = "A short story of the Expanse.";
            audiobook.Series = "The Expanse";
            audiobook.SeriesNumber = "2.7";
            audiobook.Files = [.. fileNames.Select(name =>
                AudiobookFile.CreateUnresolved(Path.Combine(_directory, name)))];

            _audiobooks.Setup(repository => repository.GetLibraryAsync()).ReturnsAsync([audiobook]);
            _audiobooks
                .Setup(repository => repository.GetAllSeriesMembershipsGroupedByAudiobookIdAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            _configuration
                .Setup(service => service.GetApplicationSettingsAsync())
                .ReturnsAsync(new ApplicationSettingsBuilder().Build());

            _fileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
            _fileSystem.Setup(fs => fs.GetFileLength(It.IsAny<string>())).Returns(1024);
            _fileSystem
                .Setup(fs => fs.GetLastWriteTimeUtc(It.IsAny<string>()))
                .Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            _writer
                .Setup(writer => writer.IsAvailableAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            _rootFolders
                .Setup(service => service.GetAllAsync())
                .ReturnsAsync([new RootFolder { Path = _directory }]);

            // No expected paths unless a test asks for them: organizing is a separate
            // concern, and the table has to build whether or not it can answer.
            _rename
                .Setup(service => service.PreviewRenameAsync(
                    It.IsAny<int[]>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            return audiobook;
        }

        private void GivenCurrentTags(params (string Key, string Value)[] tags) =>
            _writer
                .Setup(writer => writer.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AudiobookFileTags(
                    tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.OrdinalIgnoreCase),
                    12,
                    TimeSpan.FromHours(9),
                    HasCoverArt: true));

        [Fact]
        public async Task BuildAsync_ReportsWhatTheFileCarriesAndWhatListenarrWouldWrite()
        {
            GivenLibrary("Drive.m4b");
            GivenCurrentTags(("album", "Drive"));

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            Assert.Equal("Drive", row.Tags[TagCatalog.Album]);
            Assert.Equal("[The Expanse 2.7] Drive", row.Expected[TagCatalog.Album]);
            Assert.Contains(TagCatalog.Album, row.Mismatched);
        }

        [Fact]
        public async Task BuildAsync_DoesNotFlagATagTheFileAlreadyHasRight()
        {
            GivenLibrary("Drive.m4b");
            GivenCurrentTags(("album", "[The Expanse 2.7] Drive"));

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            Assert.DoesNotContain(TagCatalog.Album, row.Mismatched);
            Assert.Equal("[The Expanse 2.7] Drive", row.Expected[TagCatalog.Album]);
        }

        /// <summary>
        /// A file carrying a container key or a tag outside the catalog must not become a
        /// column of its own: the table's headings come from the catalog, and a row that
        /// reported <c>major_brand</c> would have nowhere to put it.
        /// </summary>
        [Fact]
        public async Task BuildAsync_KeepsOnlyCatalogTags()
        {
            GivenLibrary("Drive.m4b");
            GivenCurrentTags(("album", "Drive"), ("major_brand", "M4B"), ("totaltracks", "12"));

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            Assert.True(row.Tags.ContainsKey(TagCatalog.Album));
            Assert.False(row.Tags.ContainsKey("major_brand"));
            Assert.False(row.Tags.ContainsKey("totaltracks"));
        }

        /// <summary>
        /// A file writing <c>series</c> in lower case and the catalog's <c>SERIES</c> are
        /// one column, not two. The library's own files carry both casings.
        /// </summary>
        [Fact]
        public async Task BuildAsync_NormalisesTagCasingToTheCatalog()
        {
            GivenLibrary("Drive.m4b");
            GivenCurrentTags(("series", "The Expanse"));

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            Assert.Equal("The Expanse", row.Tags[TagCatalog.Series]);
        }

        [Fact]
        public async Task BuildAsync_ListsAnMp3ButMarksItUnwritable()
        {
            GivenLibrary("Chapter 1.mp3");
            GivenCurrentTags(("album", "Drive"));

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            Assert.False(row.Writable);
            Assert.Equal("mp3", row.Extension);
        }

        [Fact]
        public async Task BuildAsync_ListsEveryFileOfAMultiFileBookSeparately()
        {
            GivenLibrary("Part 1.m4b", "Part 2.m4b");
            GivenCurrentTags(("album", "Drive"));

            var index = await BuildService().BuildAsync();

            Assert.Equal(2, index.Rows.Count);
            Assert.Equal(["Part 1.m4b", "Part 2.m4b"], index.Rows.Select(row => row.FileName).Order());
        }

        [Fact]
        public async Task BuildAsync_IgnoresFilesThatAreNotAudio()
        {
            GivenLibrary("Drive.m4b", "cover.jpg", "book.nfo");
            GivenCurrentTags(("album", "Drive"));

            var index = await BuildService().BuildAsync();

            Assert.Equal("Drive.m4b", Assert.Single(index.Rows).FileName);
        }

        /// <summary>
        /// The cache is what makes the second load of a several-hundred-file library
        /// instant, and it is keyed on the file rather than on a clock, so it holds only
        /// as long as the file is unchanged.
        /// </summary>
        [Fact]
        public async Task BuildAsync_ReadsAFileOnceWhileItIsUnchanged()
        {
            GivenLibrary("Drive.m4b");
            GivenCurrentTags(("album", "Drive"));

            var service = BuildService();
            Assert.Equal(1, (await service.BuildAsync()).FilesRead);
            Assert.Equal(0, (await service.BuildAsync()).FilesRead);

            _writer.Verify(
                writer => writer.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task BuildAsync_ReadsAgainOnceTheFileHasChanged()
        {
            GivenLibrary("Drive.m4b");
            GivenCurrentTags(("album", "Drive"));

            var service = BuildService();
            await service.BuildAsync();

            _fileSystem
                .Setup(fs => fs.GetLastWriteTimeUtc(It.IsAny<string>()))
                .Returns(new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(1, (await service.BuildAsync()).FilesRead);
        }

        [Fact]
        public async Task BuildAsync_RefreshRereadsEvenAnUnchangedFile()
        {
            GivenLibrary("Drive.m4b");
            GivenCurrentTags(("album", "Drive"));

            var service = BuildService();
            await service.BuildAsync();

            Assert.Equal(1, (await service.BuildAsync(refresh: true)).FilesRead);
        }

        [Fact]
        public async Task BuildAsync_ReportsAFileItCouldNotRead_RatherThanAnEmptyRow()
        {
            GivenLibrary("Drive.m4b");
            _writer
                .Setup(writer => writer.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("the share went away"));

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            Assert.NotNull(row.Error);
            Assert.Empty(row.Tags);
            // Nothing was read, so nothing can be said about what should be written. A
            // row of proposals beside unknown current values would read as a diff.
            Assert.Empty(row.Mismatched);
        }

        [Fact]
        public async Task BuildAsync_ReportsAMissingFileWithoutProbingIt()
        {
            GivenLibrary("Drive.m4b");
            _fileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(false);

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            Assert.NotNull(row.Error);
            _writer.Verify(
                writer => writer.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// A cancelled load must not return while probes are still running.
        /// </summary>
        /// <remarks>
        /// <c>Task.WhenAll</c> rethrows on the first cancellation while the rest are in
        /// flight, and the semaphore is disposed on the way out. Returning there would
        /// abandon running ffprobe processes with nothing left to close their pipes —
        /// several hundred files' worth of descriptors, on a page an operator can navigate
        /// away from at any moment.
        /// </remarks>
        [Fact]
        public async Task BuildAsync_WaitsForEveryProbeToSettleBeforeItGivesUp()
        {
            GivenLibrary("Part 1.m4b", "Part 2.m4b", "Part 3.m4b", "Part 4.m4b", "Part 5.m4b");

            using var cancellation = new CancellationTokenSource();
            var running = 0;
            var peakRunning = 0;

            _writer
                .Setup(writer => writer.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    peakRunning = Math.Max(peakRunning, Interlocked.Increment(ref running));
                    try
                    {
                        await Task.Delay(40, CancellationToken.None);
                        await cancellation.CancelAsync();
                        return AudiobookFileTags.Empty;
                    }
                    finally
                    {
                        Interlocked.Decrement(ref running);
                    }
                });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => BuildService().BuildAsync(cancellationToken: cancellation.Token));

            // The point: nothing is still in flight once the call has returned.
            Assert.Equal(0, Volatile.Read(ref running));
            Assert.True(peakRunning > 0, "the test never actually started a probe");
        }

        [Fact]
        public async Task BuildAsync_SaysSoWhenThereIsNoProbe()
        {
            GivenLibrary("Drive.m4b");
            _writer
                .Setup(writer => writer.IsAvailableAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            Assert.NotNull(row.Error);
            Assert.Empty(row.Tags);
        }

        /// <summary>
        /// The lock the table draws, and the write it stops, have to be the same fact.
        /// </summary>
        [Fact]
        public async Task BuildAsync_ReportsALockedTagAndStopsCountingItAsAMismatch()
        {
            var audiobook = GivenLibrary("Drive.m4b");
            audiobook.Files![0].LockedTags = ["album"];
            GivenCurrentTags(("album", "Whatever the release called it"));

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            // Catalog casing, whatever casing was stored.
            Assert.Equal([TagCatalog.Album], row.LockedTags);
            Assert.DoesNotContain(TagCatalog.Album, row.Mismatched);

            // The value is still reported: a locked cell shows what the file holds, it
            // just stops claiming the file is wrong.
            Assert.Equal("Whatever the release called it", row.Tags[TagCatalog.Album]);
        }

        [Fact]
        public async Task BuildAsync_ShowsWhereOrganizingWouldPutTheFile()
        {
            var audiobook = GivenLibrary("Drive.m4b");
            var file = audiobook.Files![0];
            var expected = Path.Combine(_directory, "James S. A. Corey", "Drive.m4b");

            _rename
                .Setup(service => service.PreviewRenameAsync(
                    It.IsAny<int[]>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([new RenamePreview
                {
                    AudiobookId = audiobook.Id,
                    FileRenames =
                    [
                        new FileRenamePreview
                        {
                            FileId = file.Id,
                            CurrentPath = file.Path,
                            NewPath = expected,
                            Changed = true
                        }
                    ]
                }]);

            GivenCurrentTags(("album", "Drive"));

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            // The folder alone, trimmed to the library root: the filename is the row's
            // own column, and a path column repeating it would truncate away the half
            // that tells one part of a book from another.
            Assert.Equal(string.Empty, row.DisplayPath);
            Assert.Equal("James S. A. Corey", row.ExpectedPath);
            Assert.True(row.PathMismatched);

            // The file would be renamed as well as moved, and the two are reported apart
            // because the table shows them apart.
            Assert.Equal("Drive.m4b", row.ExpectedFileName);
            Assert.False(row.FileNameMismatched);
        }

        /// <summary>
        /// Organizing has its own reasons to be unavailable — an unregistered service, a
        /// root whose filesystem identity cannot be established — and none of them is a
        /// reason to refuse to show the library's tags.
        /// </summary>
        [Fact]
        public async Task BuildAsync_StillBuildsWhenOrganizingCannotAnswer()
        {
            GivenLibrary("Drive.m4b");
            GivenCurrentTags(("album", "Drive"));

            _rename
                .Setup(service => service.PreviewRenameAsync(
                    It.IsAny<int[]>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("That root has no usable identity."));

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            // Unknown, which is not the same as "already in the right place" and must not
            // be drawn as agreement.
            Assert.Null(row.ExpectedPath);
            Assert.Null(row.ExpectedFileName);
            Assert.False(row.PathMismatched);
            Assert.False(row.FileNameMismatched);
        }

        [Fact]
        public async Task BuildAsync_ReportsAFileWhosePathIsLocked()
        {
            var audiobook = GivenLibrary("Drive.m4b");
            audiobook.Files![0].PathLocked = true;
            GivenCurrentTags(("album", "Drive"));

            var row = Assert.Single((await BuildService().BuildAsync()).Rows);

            Assert.True(row.PathLocked);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
