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
    /// The before-and-after an operator approves before anything is written.
    ///
    /// The property that matters is that the preview is produced by the same planner the
    /// worker uses, against the file's real current tags. A preview derived from a
    /// separate approximation would eventually disagree with the write, and an operator
    /// who has approved a diff is entitled to get that diff.
    /// </summary>
    [Trait("Name", "TagPreviewServiceTests")]
    [Trait("Category", "Tagging")]
    public sealed class TagPreviewServiceTests : BaseTests, IDisposable
    {
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IAudiobookTagWriter> _writer = new();
        private readonly Mock<IFileSystem> _fileSystem = new();

        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "listenarr-preview-" + Guid.NewGuid().ToString("N"));

        private TagPreviewService BuildService() => new(
            _audiobooks.Object,
            _configuration.Object,
            _writer.Object,
            new AudiobookTagPlanner(new FileNamingService(
                _configuration.Object,
                NullLogger<FileNamingService>.Instance)),
            _fileSystem.Object,
            NullLogger<TagPreviewService>.Instance);

        private Audiobook GivenAudiobook(params string[] fileNames)
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
            {
                File.WriteAllText(Path.Combine(_directory, name), "not really audio");
                return AudiobookFile.CreateUnresolved(Path.Combine(_directory, name));
            })];

            _audiobooks.Setup(repository => repository.GetByIdAsync(7)).ReturnsAsync(audiobook);
            _fileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
            _configuration
                .Setup(service => service.GetApplicationSettingsAsync())
                .ReturnsAsync(new ApplicationSettingsBuilder().Build());
            _writer
                .Setup(writer => writer.IsAvailableAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

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
        public async Task BuildAsync_ShowsTheCurrentAndProposedValueForEachTag()
        {
            GivenAudiobook("Drive.m4b");
            GivenCurrentTags(("album", "Drive"), ("description", "The release's own blurb."));

            var preview = await BuildService().BuildAsync(7);

            Assert.True(preview.CanWrite);
            var file = Assert.Single(preview.Files);

            var album = file.Changes.Single(change => change.Tag == TagCatalog.Album);
            Assert.Equal("Drive", album.Current);
            Assert.Equal("[The Expanse 2.7] Drive", album.Proposed);
            Assert.Equal(TagChangeAction.Write, album.Action);

            var description = file.Changes.Single(change => change.Tag == TagCatalog.Description);
            Assert.Equal("The release's own blurb.", description.Current);
            Assert.Equal("A short story of the Expanse.", description.Proposed);
        }

        [Fact]
        public async Task BuildAsync_ReportsATagThatWillNotChange()
        {
            GivenAudiobook("Drive.m4b");
            GivenCurrentTags(("album", "[The Expanse 2.7] Drive"));

            var preview = await BuildService().BuildAsync(7);

            var album = Assert.Single(preview.Files).Changes
                .Single(change => change.Tag == TagCatalog.Album);
            Assert.Equal(TagChangeAction.Unchanged, album.Action);
        }

        [Fact]
        public async Task BuildAsync_NarrowsToTheSelectedTags()
        {
            GivenAudiobook("Drive.m4b");
            GivenCurrentTags(("album", "Drive"));

            var preview = await BuildService().BuildAsync(7, [TagCatalog.Description]);

            var changes = Assert.Single(preview.Files).Changes;
            Assert.Equal(
                TagChangeAction.Deselected,
                changes.Single(change => change.Tag == TagCatalog.Album).Action);
            Assert.Equal(
                TagChangeAction.Write,
                changes.Single(change => change.Tag == TagCatalog.Description).Action);
        }

        [Fact]
        public async Task BuildAsync_PreviewsEveryFileOfAMultiFileBook()
        {
            GivenAudiobook("Part 1.m4b", "Part 2.m4b");
            GivenCurrentTags(("album", "Drive"));

            var preview = await BuildService().BuildAsync(7);

            Assert.Equal(2, preview.Files.Count);
            Assert.All(preview.Files, file => Assert.True(file.HasChanges));
        }

        [Fact]
        public async Task BuildAsync_IgnoresFilesThatCannotCarryTheseTags()
        {
            GivenAudiobook("Chapter 1.mp3");

            var preview = await BuildService().BuildAsync(7);

            Assert.False(preview.CanWrite);
            Assert.Empty(preview.Files);
            Assert.NotNull(preview.Reason);
        }

        [Fact]
        public async Task BuildAsync_ReportsAFileItCouldNotRead_RatherThanShowingAnEmptyDiff()
        {
            GivenAudiobook("Drive.m4b");
            _writer
                .Setup(writer => writer.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("the share went away"));

            var preview = await BuildService().BuildAsync(7);

            var file = Assert.Single(preview.Files);
            Assert.NotNull(file.Error);
            Assert.Empty(file.Changes);
            Assert.False(preview.CanWrite);
        }

        [Fact]
        public async Task BuildAsync_SaysSoWhenThereIsNoWriter()
        {
            GivenAudiobook("Drive.m4b");
            _writer
                .Setup(writer => writer.IsAvailableAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var preview = await BuildService().BuildAsync(7);

            Assert.False(preview.CanWrite);
            Assert.NotNull(preview.Reason);
        }

        [Fact]
        public async Task BuildAsync_WritesNothingAndQueuesNothing()
        {
            // A preview that had a side effect would be a strange thing to offer as a way
            // of deciding whether to have one.
            GivenAudiobook("Drive.m4b");
            GivenCurrentTags(("album", "Drive"));

            await BuildService().BuildAsync(7);

            _writer.Verify(
                writer => writer.WriteAsync(
                    It.IsAny<TagWriteRequest>(),
                    It.IsAny<IProgress<double>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _writer.Verify(
                writer => writer.ApplyAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task BuildAsync_SaysSoWhenTheBookHasGone()
        {
            _audiobooks.Setup(repository => repository.GetByIdAsync(7)).ReturnsAsync((Audiobook?)null);

            var preview = await BuildService().BuildAsync(7);

            Assert.False(preview.CanWrite);
            Assert.NotNull(preview.Reason);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not a test failure.
            }
        }
    }
}
