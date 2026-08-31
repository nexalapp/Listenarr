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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Tagging
{
    /// <summary>
    /// Rendering a tag value from a naming pattern.
    ///
    /// The point of sharing the pattern language with file naming is that the album tag
    /// can mirror the folder name; the point of not sharing the <em>output</em> rules is
    /// that a tag may hold a colon, a slash and a paragraph break, none of which survive
    /// a path component. These cover both halves of that.
    /// </summary>
    [Trait("Name", "FileNamingService_TagRenderingTests")]
    [Trait("Category", "Tagging")]
    public class FileNamingService_TagRenderingTests : BaseTests
    {
        private static FileNamingService CreateService() =>
            new(new Mock<IConfigurationService>().Object, new Mock<ILogger<FileNamingService>>().Object);

        private static AudioMetadata Book(
            string title = "Drive",
            string? series = "The Expanse",
            string? position = "2.7") =>
            new()
            {
                Title = title,
                Artist = "James S. A. Corey",
                AlbumArtist = "James S. A. Corey",
                Narrator = "Jefferson Mays",
                Series = series,
                SeriesPositionRaw = position,
                AllSeries = series == null ? null : [new SeriesReference(series, position)]
            };

        // ---- the library's own album convention -------------------------------------

        [Fact]
        public void SeriesBook_RendersTheBracketedAlbumTheLibraryUses()
        {
            // Every series book in the real library is tagged this way, and the folder is
            // named to match. A pattern that produced a plain title would disagree with
            // several hundred already-correct files.
            var value = CreateService().RenderTagValue("{SeriesBrackets} {Title}", Book());

            Assert.Equal("[The Expanse 2.7] Drive", value);
        }

        [Fact]
        public void StandaloneBook_CollapsesTheBracketGroupAway()
        {
            // The same pattern, no conditional: an empty token takes its brackets and the
            // separator next to it with it.
            var value = CreateService().RenderTagValue(
                "{SeriesBrackets} {Title}",
                Book("Radicalized", series: null, position: null));

            Assert.Equal("Radicalized", value);
        }

        [Fact]
        public void MultiSeriesBook_RendersOneBracketGroupPerSeries()
        {
            // The double-bracket form the library's cross-series books already carry.
            // The primary series alone cannot reconstruct it, which is why the token
            // reads every membership rather than just Audiobook.Series.
            var book = Book("A War Of Gifts", "Enderverse", "07.5");
            book.AllSeries =
            [
                new SeriesReference("Enderverse", "07.5"),
                new SeriesReference("Ender's Saga", "1.1")
            ];

            var value = CreateService().RenderTagValue("{SeriesBrackets} {Title}", book);

            Assert.Equal("[Enderverse 07.5][Ender's Saga 1.1] A War Of Gifts", value);
        }

        [Fact]
        public void SortAlbum_RendersBracketlessAndSortable()
        {
            var value = CreateService().RenderTagValue("{Series} {SeriesNumber} - {Title}", Book());

            Assert.Equal("The Expanse 2.7 - Drive", value);
        }

        [Fact]
        public void SortAlbum_DropsTheSeparatorForAStandalone()
        {
            var value = CreateService().RenderTagValue(
                "{Series} {SeriesNumber} - {Title}",
                Book("Radicalized", series: null, position: null));

            Assert.Equal("Radicalized", value);
        }

        // ---- what a tag may hold that a filename may not ----------------------------

        [Fact]
        public void ColonsSurvive_BecauseATagIsNotAPathComponent()
        {
            // Path naming rewrites ':' to ' - ' because a filename cannot carry it. Doing
            // that to a tag would silently rename the book inside its own file.
            var value = CreateService().RenderTagValue(
                "{Title}",
                Book("Book Two: The Reckoning", series: null, position: null));

            Assert.Equal("Book Two: The Reckoning", value);
        }

        [Fact]
        public void DescriptionKeepsItsParagraphs()
        {
            // The blurb is the reason this exists. Collapsing its newlines the way a
            // filename would turns a summary into one run-on line in Plex.
            var book = Book();
            book.Description = "A gripping opener.\n\nThe second paragraph.";

            var value = CreateService().RenderTagValue("{Description}", book);

            Assert.Equal("A gripping opener.\n\nThe second paragraph.", value);
        }

        [Fact]
        public void ForwardSlashesSurvive_BecauseTheyAreNotPathSeparatorsHere()
        {
            var book = Book();
            book.Description = "Science Fiction / Space Opera";

            var value = CreateService().RenderTagValue("{Description}", book);

            Assert.Equal("Science Fiction / Space Opera", value);
        }

        // ---- patterns whose tokens all resolve empty --------------------------------

        [Fact]
        public void PatternWithNoResolvedTokens_RendersNothing()
        {
            // Otherwise a book with no ASIN gets "https://www.audible.com/pd/" written
            // into a tag: the scaffolding without the value it was framing.
            var value = CreateService().RenderTagValue(
                "https://www.audible.com/pd/{Asin}",
                Book(series: null, position: null));

            Assert.Equal(string.Empty, value);
        }

        [Fact]
        public void PatternWithOneResolvedToken_KeepsItsLiterals()
        {
            var book = Book();
            book.Asin = "B0F7Y6JB13";

            var value = CreateService().RenderTagValue("https://www.audible.com/pd/{Asin}", book);

            Assert.Equal("https://www.audible.com/pd/B0F7Y6JB13", value);
        }

        [Fact]
        public void PatternWithNoTokensAtAll_IsWrittenAsWritten()
        {
            // A literal is a deliberate choice by whoever typed it, not an unresolved
            // token, so the empty-token rule does not apply to it.
            var value = CreateService().RenderTagValue("Audiobook", Book());

            Assert.Equal("Audiobook", value);
        }

        [Fact]
        public void EmptyPattern_RendersNothing()
        {
            Assert.Equal(string.Empty, CreateService().RenderTagValue(string.Empty, Book()));
        }

        // ---- the path renderer is unaffected ----------------------------------------

        [Fact]
        public void PathRendering_StillSanitisesWhatATagKeeps()
        {
            // The two modes share the substitution and the collapse, and must not share
            // anything else. A regression here would put a colon into a filename.
            var value = CreateService().ApplyNamingPattern(
                "{Title}",
                Book("Book Two: The Reckoning", series: null, position: null),
                treatAsFilename: true);

            Assert.Equal("Book Two - The Reckoning", value);
        }
    }
}
