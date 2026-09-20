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
using Listenarr.Domain.FoundBooks;
using Listenarr.Infrastructure.FoundBooks.Scanning;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.FoundBooks
{
    /// <summary>
    /// The scanner against a folder shaped like a real pack: loose files from several
    /// books, a book split between loose files and a subfolder, a partial, an untagged
    /// folder, and a download still in flight.
    /// </summary>
    [Trait("Name", "FoundBookScannerTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookScannerTests : BaseTests
    {
        private readonly FakeProbe _probe = new();
        private string _watch = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _watch = FileService.GetTempDirectory("watch");
        }

        private FoundBookWatchFolder Folder => new(_watch, FileSystemPathSemantics.CurrentHostDefault);

        private FoundBookScanner BuildScanner() => new(
            _provider.GetRequiredService<IFileSystem>(),
            _probe,
            _provider.GetRequiredService<IConfigurationService>(),
            NullLogger<FoundBookScanner>.Instance);

        private async Task<string> Audio(string relative, string? album, string? artist, double seconds = 600, string? comment = null)
        {
            var directory = Path.GetDirectoryName(Path.Join(_watch, relative))!;
            Directory.CreateDirectory(directory);
            var path = await FileService.GetFileAsync(directory, Path.GetFileName(relative), "audio");
            _probe.Tags[path] = new FoundBookProbeSnapshot(
                true, null, seconds, 0, null, null, null, album, artist, null, null, null, null, null, null,
                DeclaredLengthParser.Parse(comment));
            return path;
        }

        private async Task<string> Other(string relative, string content = "x")
        {
            var directory = Path.GetDirectoryName(Path.Join(_watch, relative))!;
            Directory.CreateDirectory(directory);
            return await FileService.GetFileAsync(directory, Path.GetFileName(relative), content);
        }

        private async Task GivenThePack()
        {
            // Loose files from three books, plus their companions.
            for (var i = 1; i <= 3; i++)
            {
                await Audio($"ENCR-ODY3 {i:00}-03.mp3", "Homeworld", "Evan Currie");
            }

            await Other("Evan Currie - Homeworld (2013).jpg");
            await Other("Evan Currie - Homeworld (2013).txt", "Odyssey One book 3");
            for (var i = 1; i <= 2; i++)
            {
                await Audio($"Evan Currie - King of Thieves {i:00}-02.mp3", "King of Thieves", "Evan Currie");
            }

            // Courageous: part one loose, part two in a folder, same album tag.
            for (var i = 1; i <= 2; i++)
            {
                await Audio($"lfl03_Courageous_01 - {i:00}.mp3", "The Lost Fleet - book 03", "Jack Campbell");
                await Audio($"Courageous 02/lfl03_Courageous_02 - {i:00}.mp3", "The Lost Fleet - book 03", "Jack Campbell");
            }

            // Parts 4–6 of something longer.
            for (var i = 4; i <= 6; i++)
            {
                await Audio($"Jack Campbell (2019) Triumphant/{i:000} Jack Campbell (2019) Triumphant.m4a", "Genesis Fleet 03", "Jack Campbell");
            }

            // No tags at all.
            for (var i = 1; i <= 3; i++)
            {
                await Audio($"New Folder With Items/Z09387_{i:000}_C000.mp3", null, null);
            }

            // Still being written by the client.
            await Audio("Still Downloading/book.mp3", "Something", "Someone");
            await Other("Still Downloading/book.mp3.part");

            // A folder-per-book rip whose nfo states the length.
            for (var i = 1; i <= 3; i++)
            {
                await Audio($"Hugh Howey - Wool (2012)/{i:00} - Wool.mp3", "Wool", "Hugh Howey");
            }

            await Other("Hugh Howey - Wool (2012)/Hugh Howey - Wool.nfo", "Length: 30 mins\nNarrated by Edoardo Ballerini");
            await Other(".DS_Store");
        }

        private static FoundBookCandidate ByTitle(FoundBookScanReport report, string title) =>
            Assert.Single(report.Candidates, c => string.Equals(c.Title, title, StringComparison.Ordinal));

        [Fact]
        public async Task Pack_IsUntangledIntoOneClusterPerBook()
        {
            await GivenThePack();

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            Assert.Equal(7, report.Candidates.Count);
            Assert.Equal(
                report.Candidates.Sum(c => c.AudioFileCount),
                Directory.EnumerateFiles(_watch, "*", SearchOption.AllDirectories).Count(f => f.EndsWith(".mp3", StringComparison.Ordinal) || f.EndsWith(".m4a", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task LooseFiles_SplitByAlbumAndKeepTheirCompanions()
        {
            await GivenThePack();

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            var homeworld = ByTitle(report, "Homeworld");
            Assert.Equal(3, homeworld.AudioFileCount);
            Assert.Equal(FoundBookCompleteness.Complete, homeworld.Completeness);
            Assert.Equal("Evan Currie", homeworld.Author);
            Assert.Equal(_watch, homeworld.BookFolder);
            Assert.Equal(2, homeworld.Files.Count(f => !f.IsAudio));

            var thieves = ByTitle(report, "King of Thieves");
            Assert.Equal(2, thieves.AudioFileCount);
            Assert.DoesNotContain(thieves.Files, f => !f.IsAudio);
        }

        [Fact]
        public async Task BookSplitAcrossLooseFilesAndAFolder_IsOneCluster()
        {
            await GivenThePack();

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            var courageous = ByTitle(report, "The Lost Fleet - book 03");
            Assert.Equal(4, courageous.AudioFileCount);
            Assert.Equal(_watch, courageous.BookFolder);
        }

        [Fact]
        public async Task PartialBook_IsIncomplete()
        {
            await GivenThePack();

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            var triumphant = ByTitle(report, "Genesis Fleet 03");
            Assert.Equal(FoundBookCompleteness.Incomplete, triumphant.Completeness);
            Assert.Contains("missing 1–3", triumphant.CompletenessReason);
        }

        [Fact]
        public async Task UntaggedFolder_IsOneUnknownClusterNamedForItsFolder()
        {
            await GivenThePack();

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            var unknown = ByTitle(report, "New Folder With Items");
            Assert.Equal(3, unknown.AudioFileCount);
            Assert.Null(unknown.Author);
            Assert.Equal(FoundBookCompleteness.Unknown, unknown.Completeness);
        }

        [Fact]
        public async Task FolderWithAPartFile_IsMarkedInProgress()
        {
            await GivenThePack();

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            var downloading = ByTitle(report, "Something");
            Assert.True(downloading.DownloadInProgress);
            Assert.DoesNotContain(downloading.Files, f => f.Path.EndsWith(".part", StringComparison.Ordinal));
        }

        [Fact]
        public async Task DeclaredLengthInTheNfo_MakesTheBookComplete()
        {
            await GivenThePack();

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            var wool = ByTitle(report, "Wool");
            Assert.Equal(FoundBookCompleteness.Complete, wool.Completeness);
            Assert.Contains("declared 30m", wool.CompletenessReason);
            Assert.Equal("2012", wool.Year);
        }

        [Fact]
        public async Task FolderName_FillsInWhatTagsLack()
        {
            await Audio("Alan Black - [Metal Boxes 03] -Rusty Hinges  (u255~55-64.44-m-chap)/Alan Black - Rusty Hinges  01--02.mp3", null, null);
            await Audio("Alan Black - [Metal Boxes 03] -Rusty Hinges  (u255~55-64.44-m-chap)/Alan Black - Rusty Hinges  02--02.mp3", null, null);

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            var book = Assert.Single(report.Candidates);
            Assert.Equal("Alan Black", book.Author);
            Assert.Equal("Rusty Hinges", book.Title);
            Assert.Equal("Metal Boxes", book.Series);
            Assert.Equal("03", book.SeriesPosition);
            Assert.Equal(FoundBookCompleteness.Complete, book.Completeness);
        }

        [Fact]
        public async Task DiscFolders_AreOneBook_ButPlainSiblingsAreNot()
        {
            await Audio("Book/CD1/01.mp3", "Illuminatus", "Shea");
            await Audio("Book/CD2/01.mp3", "Illuminatus", "Shea");
            await Audio("Series/Volume One/01.mp3", "Red Dwarf", "Naylor");
            await Audio("Series/Volume Two/01.mp3", "Red Dwarf", "Naylor");

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            var illuminatus = ByTitle(report, "Illuminatus");
            Assert.Equal(2, illuminatus.AudioFileCount);
            Assert.Equal(Path.Join(_watch, "Book"), illuminatus.BookFolder);
            Assert.Equal(2, report.Candidates.Count(c => c.Title == "Red Dwarf"));
        }

        [Fact]
        public async Task HashNamedFolder_IsNamedFromItsFilenames()
        {
            // A download client's job folder says nothing; the files inside say everything.
            await Audio("5da8cead8779ae4719256549/Jack Campbell - The Lost Fleet 02 - Fearless - Unabridged - Part 1.mp3", null, null);
            await Audio("5da8cead8779ae4719256549/Jack Campbell - The Lost Fleet 02 - Fearless - Unabridged - Part 2.mp3", null, null);

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            var book = Assert.Single(report.Candidates);
            Assert.Equal("Fearless", book.Title);
            Assert.Equal("Jack Campbell", book.Author);
            Assert.Equal("The Lost Fleet", book.Series);
            Assert.Equal("02", book.SeriesPosition);
        }

        [Fact]
        public async Task UntaggedPartsNumberedInParentheses_AreOneBook()
        {
            for (var i = 1; i <= 3; i++)
            {
                await Audio($"Foundation and Earth/Isaac Asimov, Foundation and Earth ({i:00} of 03).mp3", null, null);
            }

            var report = await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>());

            var book = Assert.Single(report.Candidates);
            Assert.Equal(3, book.AudioFileCount);
            Assert.Equal("Foundation and Earth", book.Title);
            Assert.Equal("Isaac Asimov", book.Author);
            Assert.Equal(FoundBookCompleteness.Complete, book.Completeness);
        }

        [Theory]
        [InlineData("Isaac Asimov, Foundation and Earth (01 of 26).mp3", "Isaac Asimov, Foundation and Earth")]
        [InlineData("Jack Campbell - The Lost Fleet 02 - Fearless - Unabridged - Part 1.mp3", "Jack Campbell - The Lost Fleet 02 - Fearless")]
        [InlineData("Hugh Howey - Wool 01-34.mp3", "Hugh Howey - Wool")]
        [InlineData("Z09387_001_C000.mp3", "Z09387")]
        [InlineData("Ithaca-Part01.mp3", "Ithaca")]
        [InlineData("01 - Swamp Spirits.mp3", "01 - Swamp Spirits")]
        public void StemText_StripsNumberingAndEdition(string file, string expected) =>
            Assert.Equal(expected, FoundBookScanner.StemText(file));

        [Fact]
        public async Task UnchangedFiles_AreNotProbedAgain()
        {
            var path = await Audio("Hugh Howey - Wool (2012)/01 - Wool.mp3", "Wool", "Hugh Howey");
            var info = new FileInfo(path);
            var known = new Dictionary<string, FoundBookKnownFile>
            {
                [path] = new(info.Length, info.LastWriteTimeUtc, _probe.Tags[path])
            };

            await BuildScanner().ScanAsync(Folder, known);

            Assert.DoesNotContain(path, _probe.Probed);
        }

        [Fact]
        public async Task ClusterKey_IgnoresSizeAndSignatureDoesNot()
        {
            var path = await Audio("Book/01 - Book.mp3", "Book", "Author", seconds: 3600);
            var first = Assert.Single((await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>())).Candidates);

            await File.WriteAllTextAsync(path, "audio that grew");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            var second = Assert.Single((await BuildScanner().ScanAsync(Folder, new Dictionary<string, FoundBookKnownFile>())).Candidates);

            Assert.Equal(first.ClusterKey, second.ClusterKey);
            Assert.NotEqual(first.Signature, second.Signature);
        }

        private sealed class FakeProbe : IFoundBookProbe
        {
            public Dictionary<string, FoundBookProbeSnapshot> Tags { get; } = new(StringComparer.Ordinal);
            public List<string> Probed { get; } = [];

            public Task<FoundBookProbeSnapshot> ProbeAsync(string path, CancellationToken cancellationToken = default)
            {
                lock (Probed)
                {
                    Probed.Add(path);
                }

                return Task.FromResult(Tags.TryGetValue(path, out var snapshot)
                    ? snapshot
                    : FfprobeFoundBookProbe.Failed("not a real file"));
            }
        }
    }
}
