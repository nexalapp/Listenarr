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
    [Trait("Name", "ChapterRebuildPlannerTests")]
    [Trait("Category", "Domain")]
    public sealed class ChapterRebuildPlannerTests : BaseTests
    {
        private static readonly TimeSpan Track = TimeSpan.FromMinutes(3);

        private static List<EmbeddedChapter> Tracks(int count) =>
            Enumerable.Range(0, count)
                .Select(i => new EmbeddedChapter($"Track {i + 1:D2}", Track * i, Track * (i + 1)))
                .ToList();

        [Fact]
        public void Merge_KeepsAnnouncedMarksAndFoldsTheRest()
        {
            // Twelve tracks; the narrator announces at tracks 1, 4, 7 and 10.
            var marks = Tracks(12);
            var heard = new string?[12];
            heard[0] = "Chapter one. The morning came.";
            heard[3] = "Chapter two.";
            heard[6] = "Chapter three.";
            heard[9] = "Chapter four.";

            var (plan, rejection) = ChapterRebuildPlanner.Merge(marks, heard, Track * 12, expectedCount: 4);

            Assert.Null(rejection);
            Assert.Equal(ChapterSource.Announcements, plan!.Source);
            Assert.Equal(["Chapter 1", "Chapter 2", "Chapter 3", "Chapter 4"], plan.Chapters.Select(c => c.Title));
            Assert.Equal([TimeSpan.Zero, Track * 3, Track * 6, Track * 9], plan.Chapters.Select(c => c.Start));
            Assert.Equal(Track * 12, plan.Chapters[^1].End);
            Assert.Contains("matches Audnexus", plan.Note);
            Assert.Equal("Chapter two.", plan.Heard![1]);
        }

        [Fact]
        public void Merge_KeepsTheFirstMarkAsAnIntroductionWhenChapterOneComesLater()
        {
            var marks = Tracks(6);
            var heard = new string?[6];
            heard[2] = "Chapter one.";
            heard[4] = "Chapter two.";

            var (plan, _) = ChapterRebuildPlanner.Merge(marks, heard, Track * 6, null);

            Assert.Equal(["Introduction", "Chapter 1", "Chapter 2"], plan!.Chapters.Select(c => c.Title));
        }

        [Fact]
        public void Merge_RefusesWhenNothingWasAnnounced()
        {
            var marks = Tracks(30);
            var heard = Enumerable.Repeat<string?>("and then she said nothing at all", 30).ToList();

            var (plan, rejection) = ChapterRebuildPlanner.Merge(marks, heard, Track * 30, null);

            Assert.Null(plan);
            Assert.Contains("No chapter announcements", rejection!.Reason);
        }

        [Fact]
        public void Merge_RefusesNumbersThatGoBackwards()
        {
            var marks = Tracks(6);
            var heard = new string?[6];
            heard[1] = "Chapter five.";
            heard[3] = "Chapter two.";

            var (plan, rejection) = ChapterRebuildPlanner.Merge(marks, heard, Track * 6, null);

            Assert.Null(plan);
            Assert.Contains("Chapter 2 was heard after Chapter 5", rejection!.Reason);
        }

        [Fact]
        public void Merge_NotesADisagreementWithAudnexusWithoutRefusing()
        {
            var marks = Tracks(6);
            var heard = new string?[6];
            heard[0] = "Chapter one.";
            heard[3] = "Chapter two.";

            var (plan, _) = ChapterRebuildPlanner.Merge(marks, heard, Track * 6, expectedCount: 9);

            Assert.Equal(2, plan!.Chapters.Count);
            Assert.Contains("Audnexus lists 9", plan.Note);
        }

        [Fact]
        public void SnapToMarks_MovesChapterOneToTheMarkWhereItIsAnnounced()
        {
            // Audible starts chapter 1 at 0:00, credits included; the rip split the
            // credits off, and the narrator says "1. Saint Nick" at 0:38.
            var marks = new List<EmbeddedChapter>
            {
                new("Chapter 001", TimeSpan.Zero, TimeSpan.FromSeconds(38)),
                new("Chapter 002", TimeSpan.FromSeconds(38), TimeSpan.FromMinutes(8)),
                new("Chapter 003", TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(24)),
                new("Chapter 004", TimeSpan.FromMinutes(24), TimeSpan.FromMinutes(30))
            };
            var edition = new List<EmbeddedChapter>
            {
                new("Chapter 1", TimeSpan.Zero, TimeSpan.FromMinutes(24)),
                new("Chapter 2", TimeSpan.FromMinutes(24), TimeSpan.FromMinutes(30))
            };
            var heard = new string?[] { "[Music]", "1. Saint Nick\nZach Morgan sat attentively.", null, "2. Enders stocking\nPeter Wiggin was supposed to." };

            var plan = ChapterRebuildPlanner.SnapToMarks(edition, marks, heard, TimeSpan.FromMinutes(30));

            Assert.NotNull(plan);
            Assert.Equal(["Introduction", "Chapter 1: Saint Nick", "Chapter 2: Enders stocking"], plan.Chapters.Select(c => c.Title));
            Assert.Equal([TimeSpan.Zero, TimeSpan.FromSeconds(38), TimeSpan.FromMinutes(24)], plan.Chapters.Select(c => c.Start));
        }

        [Fact]
        public void SnapToMarks_LeavesAChapterWhereItIsWhenTheNextMarkAnnouncesADifferentOne()
        {
            var marks = new List<EmbeddedChapter>
            {
                new("A", TimeSpan.Zero, TimeSpan.FromMinutes(1)),
                new("B", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10))
            };
            var edition = new List<EmbeddedChapter>
            {
                new("Chapter 1", TimeSpan.Zero, TimeSpan.FromMinutes(1)),
                new("Chapter 2", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10))
            };
            var heard = new string?[] { "Chapter one.", "Chapter two." };

            var plan = ChapterRebuildPlanner.SnapToMarks(edition, marks, heard, TimeSpan.FromMinutes(10));

            Assert.Equal(["Chapter 1", "Chapter 2"], plan!.Chapters.Select(c => c.Title));
        }

        [Fact]
        public void Retitle_KeepsEveryMarkAndNamesTheAnnouncedOnes()
        {
            var marks = new List<EmbeddedChapter>
            {
                new(" Chapter 001  - 00:00:38", TimeSpan.Zero, TimeSpan.FromSeconds(38)),
                new(" Chapter 002  - 00:07:48", TimeSpan.FromSeconds(38), TimeSpan.FromSeconds(506)),
                new(" Chapter 003  - 00:06:20", TimeSpan.FromSeconds(506), TimeSpan.FromSeconds(886))
            };
            var heard = new string?[] { "A War of Gifts, by Orson Scott Card.", "Chapter one. Zeck Morgan.", null };

            var (plan, rejection) = ChapterRebuildPlanner.Retitle(marks, heard, TimeSpan.FromSeconds(886));

            Assert.Null(rejection);
            Assert.Equal(["Introduction", "Chapter 1: Zeck Morgan", " Chapter 003  - 00:06:20"], plan!.Chapters.Select(c => c.Title));
            Assert.Equal(3, plan.Chapters.Count);
            Assert.True(plan.Partial);
            Assert.Contains("1 of 3", plan.Note);
        }

        [Fact]
        public void Retitle_RefusesWhenNothingWasAnnounced()
        {
            var (plan, rejection) = ChapterRebuildPlanner.Retitle(Tracks(3), [null, null, null], Track * 3);
            Assert.Null(plan);
            Assert.NotNull(rejection);
        }
    }
}
