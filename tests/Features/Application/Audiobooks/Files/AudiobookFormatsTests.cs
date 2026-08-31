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

namespace Listenarr.Tests.Features.Application.Audiobooks.Files
{
    [Trait("Name", "AudiobookFormatsTests")]
    [Trait("Category", "Application")]
    public sealed class AudiobookFormatsTests : BaseTests
    {
        private static AudiobookFormatSummary File(
            string? format = null,
            string? container = null,
            string? path = null) =>
            new() { AudiobookId = 1, Format = format, Container = container, Path = path };

        [Fact]
        public void Describe_ReportsTheFormatOfASingleFormatBook()
        {
            Assert.Equal(["MP3"], AudiobookFormats.Describe([File(format: "MP3")]));
        }

        [Fact]
        public void Describe_CollapsesRepeatedFormatsToOne()
        {
            Assert.Equal(
                ["M4B"],
                AudiobookFormats.Describe([File(format: "M4B"), File(format: "M4B")]));
        }

        [Fact]
        public void Describe_KeepsEveryFormatOfAMixedBook()
        {
            // A book part-way through a conversion holds both, and dropping either would
            // hide it from the filter someone is using to find exactly that.
            Assert.Equal(
                ["M4B", "MP3"],
                AudiobookFormats.Describe([File(format: "MP3"), File(format: "M4B")]));
        }

        [Fact]
        public void Describe_NormalisesCaseAndWhitespace()
        {
            Assert.Equal(["MP3"], AudiobookFormats.Describe([File(format: " mp3 ")]));
        }

        [Fact]
        public void Describe_FallsBackToTheContainer_WhenNoFormatWasExtracted()
        {
            Assert.Equal(["M4B"], AudiobookFormats.Describe([File(container: "M4B")]));
        }

        [Fact]
        public void Describe_FallsBackToTheExtension_WhenNeitherWasExtracted()
        {
            // Files registered before format extraction ran still have a usable answer
            // sitting in their own path.
            Assert.Equal(
                ["MP3"],
                AudiobookFormats.Describe([File(path: "/library/book/chapter one.mp3")]));
        }

        [Fact]
        public void Describe_PrefersAnExtractedFormatOverTheExtension()
        {
            // The extension is the weakest evidence: a mislabelled file is exactly why
            // ffprobe reads the container in the first place.
            Assert.Equal(
                ["M4B"],
                AudiobookFormats.Describe([File(format: "M4B", path: "/library/book/book.mp3")]));
        }

        [Fact]
        public void Describe_ReturnsNothingForABookWithNoFiles()
        {
            Assert.Empty(AudiobookFormats.Describe([]));
            Assert.Empty(AudiobookFormats.Describe(null));
        }

        [Fact]
        public void Describe_SkipsAFileThatOffersNoEvidenceAtAll()
        {
            Assert.Equal(
                ["MP3"],
                AudiobookFormats.Describe([File(format: "MP3"), File(path: "/library/book/cover")]));
        }
    }
}
