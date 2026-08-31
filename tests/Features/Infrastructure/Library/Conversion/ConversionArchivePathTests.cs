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
using Listenarr.Infrastructure.Library.Conversion;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Conversion
{
    [Trait("Name", "ConversionArchivePathTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class ConversionArchivePathTests : BaseTests
    {
        private static Audiobook Book(string? basePath, string title = "The Garden of Rama") =>
            new AudiobookBuilder()
                .WithId(2)
                .WithTitle(title)
                .WithBasePath(basePath!)
                .Build();

        private static RootFolder Root(string path) => new() { Path = path };

        [Fact]
        public void BuildArchiveRelativePath_MirrorsTheBooksPlaceInTheLibrary()
        {
            // The archive keeps Author/Title so a few hundred archived books stay
            // navigable and can be read back without reconstructing their origin.
            var relative = ConversionJobProcessor.BuildArchiveRelativePath(
                Book("/audiobooks/Arthur C. Clarke/[Rama 03] The Garden of Rama (1991)"),
                [Root("/audiobooks")]);

            Assert.Equal(
                Path.Combine("Arthur C. Clarke", "[Rama 03] The Garden of Rama (1991)"),
                relative);
        }

        [Fact]
        public void BuildArchiveRelativePath_KeepsEveryLevelOfADeeperPattern()
        {
            // The default folder pattern is {Author}/{Series}/{Title}, so the archive
            // must not assume two levels.
            var relative = ConversionJobProcessor.BuildArchiveRelativePath(
                Book("/audiobooks/Jarom Strong/Paragon Space/Starship Salvager"),
                [Root("/audiobooks")]);

            Assert.Equal(
                Path.Combine("Jarom Strong", "Paragon Space", "Starship Salvager"),
                relative);
        }

        [Fact]
        public void BuildArchiveRelativePath_LeavesTheOriginalFolderNamesAlone()
        {
            // Brackets, braces and apostrophes are part of how the library names things;
            // sanitising them would make the archive stop matching the library.
            var relative = ConversionJobProcessor.BuildArchiveRelativePath(
                Book("/audiobooks/Orson Scott Card/[Enderverse 07.5] A War Of Gifts {Scott Brick} (2007)"),
                [Root("/audiobooks")]);

            Assert.Equal(
                Path.Combine("Orson Scott Card", "[Enderverse 07.5] A War Of Gifts {Scott Brick} (2007)"),
                relative);
        }

        [Fact]
        public void BuildArchiveRelativePath_PrefersTheDeepestContainingRoot()
        {
            var relative = ConversionJobProcessor.BuildArchiveRelativePath(
                Book("/media/audiobooks/Author/Book"),
                [Root("/media"), Root("/media/audiobooks")]);

            Assert.Equal(Path.Combine("Author", "Book"), relative);
        }

        [Fact]
        public void BuildArchiveRelativePath_FallsBackToTheTitle_WhenOutsideEveryRoot()
        {
            // An archive that keeps the files under an ugly name beats one that refuses
            // to take them.
            var relative = ConversionJobProcessor.BuildArchiveRelativePath(
                Book("/elsewhere/Author/Book"),
                [Root("/audiobooks")]);

            Assert.Equal("The Garden of Rama 2", relative);
        }

        [Fact]
        public void BuildArchiveRelativePath_FallsBackToTheTitle_WhenTheBookHasNoBasePath()
        {
            Assert.Equal(
                "The Garden of Rama 2",
                ConversionJobProcessor.BuildArchiveRelativePath(Book(null), [Root("/audiobooks")]));
        }

        [Fact]
        public void BuildArchiveRelativePath_FallsBackWhenNoRootsAreConfigured()
        {
            Assert.Equal(
                "The Garden of Rama 2",
                ConversionJobProcessor.BuildArchiveRelativePath(
                    Book("/audiobooks/Author/Book"),
                    []));
        }

        [Fact]
        public void BuildArchiveRelativePath_NeverEscapesTheArchiveRoot()
        {
            // A book resolving above its root would put the relative path outside the
            // archive entirely, so it must fall back instead.
            var relative = ConversionJobProcessor.BuildArchiveRelativePath(
                Book("/audiobooks"),
                [Root("/audiobooks/Author/Book")]);

            Assert.DoesNotContain("..", relative);
            Assert.Equal("The Garden of Rama 2", relative);
        }

        // ---- operation identity ------------------------------------------------------

        [Fact]
        public void BuildOperationId_IsStableForTheSameJob()
        {
            // A crash mid-publication must resume the same durable operation rather than
            // start a second one, which is the whole reason this is derived and not random.
            var job = Guid.NewGuid();

            Assert.Equal(
                ConversionJobProcessor.BuildOperationId(job, 2, "/library/book/book.m4b"),
                ConversionJobProcessor.BuildOperationId(job, 2, "/library/book/book.m4b"));
        }

        [Fact]
        public void BuildOperationId_DiffersBetweenJobsForTheSameBook()
        {
            // The durable journal records this operation's source path, which is the
            // job's own scratch file. A key shared across jobs made a later conversion
            // collide with an earlier one's journal row and be rejected for a path
            // mismatch — leaving a book unconvertible forever after its first failure.
            var first = ConversionJobProcessor.BuildOperationId(
                Guid.NewGuid(), 2, "/library/book/book.m4b");
            var second = ConversionJobProcessor.BuildOperationId(
                Guid.NewGuid(), 2, "/library/book/book.m4b");

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void BuildOperationId_DiffersBetweenDestinations()
        {
            var job = Guid.NewGuid();

            Assert.NotEqual(
                ConversionJobProcessor.BuildOperationId(job, 2, "/library/book/one.m4b"),
                ConversionJobProcessor.BuildOperationId(job, 2, "/library/book/two.m4b"));
        }

        [Fact]
        public void BuildOperationId_DiffersBetweenBooks()
        {
            var job = Guid.NewGuid();

            Assert.NotEqual(
                ConversionJobProcessor.BuildOperationId(job, 2, "/library/book/book.m4b"),
                ConversionJobProcessor.BuildOperationId(job, 3, "/library/book/book.m4b"));
        }
    }
}
