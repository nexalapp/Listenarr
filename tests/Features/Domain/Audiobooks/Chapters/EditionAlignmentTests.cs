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
    [Trait("Name", "EditionAlignmentTests")]
    [Trait("Category", "Domain")]
    public sealed class EditionAlignmentTests : BaseTests
    {
        private static readonly TimeSpan Duration = TimeSpan.FromHours(2);

        /// <summary>Chapter lengths in minutes: uneven, as a book's are, so no shift of the list lands on itself.</summary>
        private static readonly int[] Lengths = [7, 9, 15, 11, 8, 14, 10, 13, 12, 16];

        /// <summary>Ten chapters of uneven length, the first at zero.</summary>
        private static List<EmbeddedChapter> Edition()
        {
            var chapters = new List<EmbeddedChapter>();
            var start = TimeSpan.Zero;
            for (var i = 0; i < Lengths.Length; i++)
            {
                var end = start + TimeSpan.FromMinutes(Lengths[i]);
                chapters.Add(new EmbeddedChapter($"Chapter {i + 1}: Title {i + 1}", start, end));
                start = end;
            }

            return chapters;
        }

        private static TimeSpan StartOf(int index) => Edition()[index].Start;

        [Fact]
        public void Align_FindsTheOffsetTheFileRunsLateBy()
        {
            var offset = TimeSpan.FromSeconds(23);
            // The file's pauses sit 23s after the edition's marks, plus noise: scene breaks.
            var pauses = Edition().Skip(1).Select(c => c.Start + offset + TimeSpan.FromSeconds(1.5))
                .Concat([TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(31), TimeSpan.FromMinutes(77)])
                .OrderBy(p => p)
                .ToList();

            var aligned = EditionAlignment.Align(Edition(), pauses, Duration);

            Assert.NotNull(aligned);
            Assert.InRange(aligned!.Offset, offset, offset + TimeSpan.FromSeconds(2));
            Assert.Equal(9, aligned.Landed);
            Assert.Equal(10, aligned.Chapters.Count);
            Assert.Equal(TimeSpan.Zero, aligned.Chapters[0].Start);
            // Snapped to the pause, not to the shifted edition mark.
            Assert.Equal(StartOf(1) + offset + TimeSpan.FromSeconds(1.5), aligned.Chapters[1].Start);
            Assert.True(aligned.Snapped[1]);
            Assert.Equal("Chapter 2: Title 2", aligned.Chapters[1].Title);
        }

        [Fact]
        public void Align_LeavesAMarkShiftedWhenNoPauseIsNear()
        {
            var pauses = Edition().Skip(1).Where((_, i) => i != 4).Select(c => c.Start).ToList();

            var aligned = EditionAlignment.Align(Edition(), pauses, Duration);

            Assert.NotNull(aligned);
            Assert.Equal(TimeSpan.Zero, aligned!.Offset);
            Assert.Equal(8, aligned.Landed);
            Assert.Equal(StartOf(5), aligned.Chapters[5].Start);
            Assert.False(aligned.Snapped[5]);
        }

        [Fact]
        public void Align_RefusesADifferentRecording()
        {
            // Pauses every seven minutes: nothing to do with the edition's chapters.
            var pauses = Enumerable.Range(1, 16).Select(i => TimeSpan.FromMinutes(7 * i)).ToList();

            Assert.Null(EditionAlignment.Align(Edition(), pauses, Duration));
        }

        [Fact]
        public void Align_RefusesAnEditionLongerThanTheFile()
        {
            var pauses = Edition().Skip(1).Select(c => c.Start).ToList();

            Assert.Null(EditionAlignment.Align(Edition(), pauses, TimeSpan.FromMinutes(90)));
        }

        [Fact]
        public void Align_DropsTheChaptersAFileThatStartsLateHasLost()
        {
            // The file is missing its first nine minutes: chapter 2 (at 7:00) is gone and chapter 3 (at 16:00) begins at 7:00.
            var offset = -TimeSpan.FromMinutes(9);
            var pauses = Edition().Skip(2).Select(c => c.Start + offset).ToList();

            var aligned = EditionAlignment.Align(Edition(), pauses, Duration);

            Assert.NotNull(aligned);
            Assert.Equal(offset, aligned!.Offset);
            // The file opens inside chapter 2, so that is what its opening chapter is called.
            Assert.Equal(["Chapter 2: Title 2", "Chapter 3: Title 3"], aligned.Chapters.Take(2).Select(c => c.Title));
            Assert.Equal(TimeSpan.FromMinutes(7), aligned.Chapters[1].Start);
        }

        [Fact]
        public void Align_IgnoresAnOffsetBeyondALeadIn()
        {
            var pauses = Edition().Skip(1).Select(c => c.Start + TimeSpan.FromMinutes(15)).ToList();

            Assert.Null(EditionAlignment.Align(Edition(), pauses, TimeSpan.FromHours(3)));
        }
    }
}
