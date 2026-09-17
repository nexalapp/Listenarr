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
using System.Runtime.InteropServices;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Services
{
    /// <summary>
    /// Tests for FileNamingService Windows path length enforcement (MAX_PATH / per-component limits)
    /// </summary>
    [Trait("Category", "FileNamingService")]
    public class FileNamingService_PathLengthTests
    {
        private readonly FileNamingService _service;

        public FileNamingService_PathLengthTests()
        {
            var mockConfig = new Mock<IConfigurationService>();
            var mockLogger = new Mock<ILogger<FileNamingService>>();
            _service = new FileNamingService(mockConfig.Object, mockLogger.Object);
        }

        [Fact]
        public void EnsurePathWithinLimits_ShortPath_ReturnsUnchanged()
        {
            var path = @"D:\Audiobooks\Author\Title\Book.m4b";
            var result = _service.EnsurePathWithinLimits(path);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Equal(path, result);
            }
        }

        [WindowsFact]
        public void EnsurePathWithinLimits_PathExceeding260Chars_IsTruncated()
        {
            // Build a path well over 260 characters
            var longAuthor = new string('A', 100);
            var longTitle = new string('T', 200);
            var path = $@"D:\Audiobooks\{longAuthor}\{longTitle}\{longTitle}.m4b";

            Assert.True(path.Length > 259, $"Test path should exceed 259 chars, was {path.Length}");

            var result = _service.EnsurePathWithinLimits(path);

            Assert.True(result.Length <= 259, $"Result path should be ≤ 259 chars, was {result.Length}");
            Assert.EndsWith(".m4b", result);
        }

        [WindowsFact]
        public void EnsurePathWithinLimits_PreservesExtension()
        {
            var longTitle = new string('T', 300);
            var path = $@"D:\Audiobooks\Author\{longTitle}.mp3";

            var result = _service.EnsurePathWithinLimits(path);

            Assert.True(result.Length <= 259);
            Assert.EndsWith(".mp3", result);
        }

        [WindowsFact]
        public void EnsurePathWithinLimits_ComponentExceeding255Chars_IsTruncated()
        {
            // Single component over 255 chars but total path under 260
            // Not realistic on Windows (260 total means components can't be that long with a root)
            // but test the per-component logic directly
            var longFolder = new string('F', 256);
            var path = $@"D:\{longFolder}\Book.m4b";

            var result = _service.EnsurePathWithinLimits(path);

            // Each component should be ≤ 255
            var parts = result.Substring(Path.GetPathRoot(result)!.Length)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                Assert.True(part.Length <= 255, $"Component '{part.Substring(0, Math.Min(30, part.Length))}...' is {part.Length} chars, exceeds 255");
            }
        }

        [WindowsFact]
        public void EnsurePathWithinLimits_TruncatesLongestComponentFirst()
        {
            // Create a path where the title folder is much longer than the author
            var shortAuthor = "Author";
            var longTitle = new string('T', 200);
            var filename = "Book.m4b";
            var path = $@"D:\Audiobooks\{shortAuthor}\{longTitle}\{filename}";

            var result = _service.EnsurePathWithinLimits(path);

            Assert.True(result.Length <= 259);
            // Author should be preserved since it's short; the long title should be truncated
            Assert.Contains(shortAuthor, result);
            Assert.EndsWith(".m4b", result);
        }

        [Fact]
        public void EnsurePathWithinLimits_EmptyOrNull_ReturnsAsIs()
        {
            Assert.Equal("", _service.EnsurePathWithinLimits(""));
            Assert.Null(_service.EnsurePathWithinLimits(null!));
        }

        [WindowsFact]
        public void EnsurePathWithinLimits_ExactlyAtLimit_ReturnsUnchanged()
        {
            // Build a path that's exactly 259 chars
            var root = @"D:\";
            var remaining = 259 - root.Length - ".m4b".Length - 1; // -1 for separator before filename
            var folder = new string('X', remaining / 2);
            var file = new string('Y', remaining - folder.Length);
            var path = $@"{root}{folder}\{file}.m4b";

            // Verify our test setup
            Assert.Equal(259, path.Length);

            var result = _service.EnsurePathWithinLimits(path);
            Assert.Equal(path, result);
        }

        /// <summary>
        /// The production failure: a full-cast narrator list rendered a filename over
        /// Linux's 255-byte NAME_MAX, which FileMover could not resolve. The pattern render
        /// itself now keeps every component under the limit, with room for the extension
        /// the caller appends, on every platform.
        /// </summary>
        [Fact]
        public void ApplyNamingPattern_FilenameWithFullCast_FitsNameMaxWithRoomForExtension()
        {
            var cast = string.Join(", ", Enumerable.Range(1, 15).Select(i => $"Narrator Number {i:00}"));
            var metadata = new AudioMetadata
            {
                Artist = "J.K. Rowling",
                Title = "Harry Potter and the Sorcerer's Stone (Full-Cast Edition)",
                Narrator = cast,
                Year = 2025,
            };

            var name = _service.ApplyNamingPattern("{Author} - {Title} {{Narrator}} ({Year})", metadata, treatAsFilename: true);

            Assert.True(System.Text.Encoding.UTF8.GetByteCount(name + ".m4b") <= 255, name);
            Assert.EndsWith(" et al.} (2025)", name);
            Assert.Contains("Narrator Number 01", name);
        }

        [Fact]
        public void ApplyNamingPattern_FolderWithFullCast_FitsNameMax()
        {
            var cast = string.Join(", ", Enumerable.Range(1, 15).Select(i => $"Narrator Number {i:00}"));
            var metadata = new AudioMetadata
            {
                Artist = "Stephen King",
                Title = "The Bazaar of Bad Dreams",
                Narrator = cast,
                Year = 2015,
            };

            var path = _service.ApplyNamingPattern("{Author}/{Title} {{Narrator}} ({Year})", metadata, treatAsFilename: false);

            foreach (var part in path.Split(Path.DirectorySeparatorChar))
            {
                Assert.True(System.Text.Encoding.UTF8.GetByteCount(part) <= 255, part);
            }
        }

        /// <summary>
        /// A co-written book is filed under its primary author. A list in the folder name
        /// gave every pair its own author folder, and in Plex an album artist that is a
        /// list is an author page nobody looks for; the full credit goes in {Authors}.
        /// </summary>
        [Fact]
        public void ApplyNamingPattern_AuthorIsThePrimaryAuthor_AuthorsIsTheWholeCredit()
        {
            var metadata = new AudioMetadata
            {
                Artist = "Larry Niven, Gregory Benford",
                Authors = ["Larry Niven", "Gregory Benford"],
                Title = "Bowl of Heaven",
            };

            Assert.Equal("Larry Niven", _service.ApplyNamingPattern("{Author}", metadata, treatAsFilename: true));
            Assert.Equal("Larry Niven, Gregory Benford", _service.ApplyNamingPattern("{Authors}", metadata, treatAsFilename: true));
        }

        [Fact]
        public void ApplyNamingPattern_AuthorFallsBackToTheFirstNameInAJoinedArtist()
        {
            var metadata = new AudioMetadata { Artist = "Larry Niven, Gregory Benford", Title = "Bowl of Heaven" };

            Assert.Equal("Larry Niven", _service.ApplyNamingPattern("{Author}", metadata, treatAsFilename: true));
        }

        /// <summary>
        /// A series files under its first book's author, so a Dune sequel by Brian
        /// Herbert lands in Frank Herbert's folder; the book's own credit stays in
        /// {Authors}. Switching the setting off returns {Author} to the book's own.
        /// </summary>
        [Fact]
        public void ApplyNamingPattern_AuthorIsTheSeriesAuthorWhenTheSettingIsOn()
        {
            var settings = new Listenarr.Application.Configuration.Contracts.ApplicationSettingsSnapshot();
            var series = new Listenarr.Application.Audiobooks.Series.SeriesAuthorSnapshot();
            series.Update(new Dictionary<string, string> { ["dune"] = "Frank Herbert" });
            var service = new FileNamingService(
                new Mock<IConfigurationService>().Object,
                new Mock<ILogger<FileNamingService>>().Object,
                settingsSnapshot: settings,
                seriesAuthors: series);
            var metadata = new AudioMetadata
            {
                Artist = "Brian Herbert, Kevin J. Anderson",
                Authors = ["Brian Herbert", "Kevin J. Anderson"],
                Series = "Dune",
                Title = "Hunters of Dune",
            };

            settings.Update(new ApplicationSettings { FileSeriesUnderFirstAuthor = true });
            Assert.Equal("Frank Herbert", service.ApplyNamingPattern("{Author}", metadata, treatAsFilename: true));
            Assert.Equal("Brian Herbert, Kevin J. Anderson", service.ApplyNamingPattern("{Authors}", metadata, treatAsFilename: true));

            settings.Update(new ApplicationSettings { FileSeriesUnderFirstAuthor = false });
            Assert.Equal("Brian Herbert", service.ApplyNamingPattern("{Author}", metadata, treatAsFilename: true));
        }
    }
}
