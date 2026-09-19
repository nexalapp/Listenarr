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
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks.Chapters
{
    [Trait("Name", "ChapterHealthAnalyzerTests")]
    [Trait("Category", "Domain")]
    public sealed class ChapterHealthAnalyzerTests : BaseTests
    {
        private static readonly ChapterAtomState SoundAtoms = new(true, null, 0, true);

        private static List<EmbeddedChapter> Evenly(int count, TimeSpan each, Func<int, string>? title = null)
        {
            var chapters = new List<EmbeddedChapter>(count);
            for (var i = 0; i < count; i++)
            {
                chapters.Add(new EmbeddedChapter(title?.Invoke(i + 1) ?? $"The {i + 1}th thing", each * i, each * (i + 1)));
            }

            return chapters;
        }

        [Fact]
        public void Analyze_NotProbed_IsUnknown()
        {
            var report = ChapterHealthAnalyzer.Analyze(null, SoundAtoms, TimeSpan.FromHours(10), "book");
            Assert.Equal(ChapterHealth.Unknown, report.Health);
        }

        [Fact]
        public void Analyze_NamedChaptersOfBookLength_AreHealthy()
        {
            var chapters = Evenly(12, TimeSpan.FromMinutes(25));
            var report = ChapterHealthAnalyzer.Analyze(chapters, SoundAtoms with { NeroChapterCount = 12 }, TimeSpan.FromHours(5), "book");

            Assert.Equal(ChapterHealth.Healthy, report.Health);
            Assert.Equal(12, report.ChapterCount);
            Assert.Equal(TimeSpan.FromMinutes(25), report.MedianLength);
        }

        [Fact]
        public void Analyze_UnparseableAtom_IsCorruptWhateverFfprobeSays()
        {
            // ffprobe falls back to the QuickTime track and reports a fine list; the atom
            // Plex reads is still broken.
            var chapters = Evenly(12, TimeSpan.FromMinutes(25));
            var atoms = new ChapterAtomState(true, "version byte is 67", 0, true);

            var report = ChapterHealthAnalyzer.Analyze(chapters, atoms, TimeSpan.FromHours(5), "book");

            Assert.Equal(ChapterHealth.Corrupt, report.Health);
            Assert.Contains("version byte is 67", report.Reason);
        }

        [Fact]
        public void Analyze_AtomCountDisagreeingWithPlayback_IsCorrupt()
        {
            var chapters = Evenly(12, TimeSpan.FromMinutes(25));
            var atoms = new ChapterAtomState(true, null, 3, true);

            var report = ChapterHealthAnalyzer.Analyze(chapters, atoms, TimeSpan.FromHours(5), "book");

            Assert.Equal(ChapterHealth.Corrupt, report.Health);
        }

        [Fact]
        public void Analyze_StructuresThatPlayAsNothing_AreCorrupt()
        {
            var report = ChapterHealthAnalyzer.Analyze([], new ChapterAtomState(false, null, 0, true), TimeSpan.FromHours(5), "book");
            Assert.Equal(ChapterHealth.Corrupt, report.Health);
        }

        [Fact]
        public void Analyze_MarksOutOfOrder_AreCorrupt()
        {
            var chapters = new List<EmbeddedChapter>
            {
                new("One", TimeSpan.Zero, TimeSpan.FromMinutes(20)),
                new("Two", TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(60)),
                new("Three", TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(40))
            };

            var report = ChapterHealthAnalyzer.Analyze(chapters, null, TimeSpan.FromHours(1), "book");

            Assert.Equal(ChapterHealth.Corrupt, report.Health);
            Assert.Contains("out of order", report.Reason);
        }

        [Fact]
        public void Analyze_MarkPastTheEnd_IsCorrupt()
        {
            var chapters = new List<EmbeddedChapter>
            {
                new("One", TimeSpan.Zero, TimeSpan.FromMinutes(20)),
                new("Two", TimeSpan.FromHours(3), TimeSpan.FromHours(3))
            };

            var report = ChapterHealthAnalyzer.Analyze(chapters, null, TimeSpan.FromHours(1), "book");

            Assert.Equal(ChapterHealth.Corrupt, report.Health);
        }

        [Fact]
        public void Analyze_ManyShortChapters_AreOversegmented()
        {
            // The Forever War shape: CD tracks that were never merged.
            var chapters = Evenly(90, TimeSpan.FromMinutes(3), i => $"Track {i:D2}");
            var report = ChapterHealthAnalyzer.Analyze(chapters, SoundAtoms with { NeroChapterCount = 90 }, TimeSpan.FromMinutes(270), "book");

            Assert.Equal(ChapterHealth.Oversegmented, report.Health);
            Assert.Contains("90 chapters", report.Reason);
        }

        [Fact]
        public void Analyze_FewShortChapters_AreNotOversegmented()
        {
            // A short story collection can have five ten-minute pieces.
            var chapters = Evenly(5, TimeSpan.FromMinutes(4));
            var report = ChapterHealthAnalyzer.Analyze(chapters, null, TimeSpan.FromMinutes(20), "book");

            Assert.NotEqual(ChapterHealth.Oversegmented, report.Health);
        }

        [Theory]
        [InlineData("Chapter 001")]
        [InlineData(" Chapter 001  - 00:00:38")]
        [InlineData("Chapter 12 - 00:06:20")]
        [InlineData("Track 03")]
        [InlineData("track 3")]
        [InlineData("")]
        public void IsPlaceholderTitle_KnowsTheRipperForms(string title) =>
            Assert.True(ChapterHealthAnalyzer.IsPlaceholderTitle(title, "book"));

        [Theory]
        [InlineData("Chapter 4: The Long Night")]
        [InlineData("Chapter 4")]     // what a narrator says, and what a retitle writes
        [InlineData("chapter 12")]
        [InlineData("Part 3")]
        [InlineData("Epilogue")]
        [InlineData("The Drop")]
        [InlineData("Part One: Departure")]
        public void IsPlaceholderTitle_KeepsRealTitles(string title) =>
            Assert.False(ChapterHealthAnalyzer.IsPlaceholderTitle(title, "book"));

        [Fact]
        public void IsPlaceholderTitle_TheFileNameIsAPlaceholder() =>
            Assert.True(ChapterHealthAnalyzer.IsPlaceholderTitle("Author - Book (2019)", "Author - Book (2019)"));

        [Fact]
        public void Analyze_AllPlaceholderTitles_AreGenericTitles()
        {
            var chapters = Evenly(25, TimeSpan.FromMinutes(6), i => $"Chapter {i:D3}  - 00:06:00");
            var report = ChapterHealthAnalyzer.Analyze(chapters, null, TimeSpan.FromMinutes(150), "book");

            Assert.Equal(ChapterHealth.GenericTitles, report.Health);
        }

        [Fact]
        public void Analyze_OneChapterTitledChapterOne_IsHealthy()
        {
            // A short story with one mark is not a placeholder problem; there is nothing
            // to retitle it with.
            var chapters = Evenly(1, TimeSpan.FromMinutes(30), _ => "Chapter 1");
            var report = ChapterHealthAnalyzer.Analyze(chapters, null, TimeSpan.FromMinutes(30), "book");

            Assert.Equal(ChapterHealth.Healthy, report.Health);
        }

        [Fact]
        public void Analyze_LongBookWithNoMarks_IsNone()
        {
            var report = ChapterHealthAnalyzer.Analyze([], new ChapterAtomState(false, null, 0, false), TimeSpan.FromHours(9), "book");
            Assert.Equal(ChapterHealth.None, report.Health);
        }

        [Fact]
        public void Analyze_ShortFileWithNoMarks_IsHealthy()
        {
            var report = ChapterHealthAnalyzer.Analyze([], null, TimeSpan.FromSeconds(37), "intro");
            Assert.Equal(ChapterHealth.Healthy, report.Health);
        }
    }
}
