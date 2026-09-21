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
    [Trait("Name", "ChapterDiscoveryPlannerTests")]
    [Trait("Category", "Domain")]
    public sealed class ChapterDiscoveryPlannerTests : BaseTests
    {
        private static readonly TimeSpan Duration = TimeSpan.FromHours(2);

        private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

        [Fact]
        public void FromAnnouncements_KeepsThePausesAChapterWasAnnouncedAfter()
        {
            var candidates = new[] { Min(3), Min(15), Min(22), Min(40), Min(58), Min(75), Min(90) };
            var heard = new string?[] { null, "Chapter two.", "the rain kept falling", "Chapter three. The drop.", null, "Chapter four.", "and so it went" };

            var (plan, rejection) = ChapterDiscoveryPlanner.FromAnnouncements(candidates, heard, "Chapter one.", Duration, edition: null);

            Assert.Null(rejection);
            Assert.Equal(ChapterSource.Announcements, plan!.Source);
            Assert.Equal(["Chapter 1", "Chapter 2", "Chapter 3: The drop", "Chapter 4"], plan.Chapters.Select(c => c.Title));
            Assert.Equal([TimeSpan.Zero, Min(15), Min(40), Min(75)], plan.Chapters.Select(c => c.Start));
            Assert.Equal(Duration, plan.Chapters[^1].End);
            Assert.False(plan.Partial);
            Assert.Contains("4 chapter(s) heard announced after 7 pause(s)", plan.Note);
        }

        [Fact]
        public void FromAnnouncements_CallsTheOpeningAnIntroductionWhenChapterOneComesLater()
        {
            var candidates = new[] { Min(2), Min(30), Min(60) };
            var heard = new string?[] { "Chapter one.", "Chapter two.", "Chapter three." };

            var (plan, _) = ChapterDiscoveryPlanner.FromAnnouncements(candidates, heard, "This is Audible.", Duration, null);

            Assert.Equal(["Introduction", "Chapter 1", "Chapter 2", "Chapter 3"], plan!.Chapters.Select(c => c.Title));
        }

        [Fact]
        public void FromAnnouncements_DropsTheEchoOfAnAnnouncementHeardAfterBothPausesAroundIt()
        {
            // "[pause] Chapter two. [pause] The morning..." — the window after the first pause hears the
            // announcement, and so does the window after the second, seven seconds on.
            var candidates = new[] { Min(20), Min(20) + TimeSpan.FromSeconds(7), Min(45), Min(70) };
            var heard = new string?[] { "Chapter two. The morning came", "Chapter two. The morning came slowly", "Chapter three.", "Chapter four." };

            var (plan, rejection) = ChapterDiscoveryPlanner.FromAnnouncements(candidates, heard, "Chapter one.", Duration, null);

            Assert.Null(rejection);
            Assert.Equal([TimeSpan.Zero, Min(20), Min(45), Min(70)], plan!.Chapters.Select(c => c.Start));
        }

        [Fact]
        public void FromAnnouncements_StartsAChapterAtItsPartHeadingAndNamesItForTheChapter()
        {
            // "[pause] Part two. [pause] Chapter five. [pause] ..." — one chapter, starting at the part heading.
            var candidates = new[] { Min(20), Min(40), Min(40) + TimeSpan.FromSeconds(6), Min(60) };
            var heard = new string?[] { "Chapter four.", "Part two.", "Chapter five.", "Chapter six." };

            var (plan, rejection) = ChapterDiscoveryPlanner.FromAnnouncements(candidates, heard, "Chapter one.", Duration, null);

            Assert.Null(rejection);
            Assert.Equal(["Chapter 1", "Chapter 4", "Chapter 5", "Chapter 6"], plan!.Chapters.Select(c => c.Title));
            Assert.Equal(Min(40), plan.Chapters[2].Start);
        }

        [Fact]
        public void FromAnnouncements_RefusesTooFewAnnouncementsAmongManyPauses()
        {
            var candidates = Enumerable.Range(1, 40).Select(i => Min(i * 2)).ToList();
            var heard = new string?[40];
            heard[5] = "Chapter two of the manual said otherwise";
            heard[30] = "Chapter three.";

            var (plan, rejection) = ChapterDiscoveryPlanner.FromAnnouncements(candidates, heard, null, Duration, null);

            Assert.Null(plan);
            Assert.Contains("Only 2 chapter announcement(s) were heard after 40 pause(s)", rejection!.Reason);
        }

        [Fact]
        public void FromAnnouncements_RefusesANarratorWhoNeverAnnounces()
        {
            var candidates = new[] { Min(10), Min(20), Min(30) };

            var (plan, rejection) = ChapterDiscoveryPlanner.FromAnnouncements(candidates, new string?[3], "This is Audible.", Duration, null);

            Assert.Null(plan);
            Assert.Contains("No chapter announcement was heard after any of the 3 pause(s)", rejection!.Reason);
        }

        [Fact]
        public void FromAnnouncements_RefusesNumbersThatDoNotClimb()
        {
            var candidates = new[] { Min(10), Min(20), Min(30), Min(40) };
            var heard = new string?[] { "Chapter two.", "Chapter three.", "Chapter two.", "Chapter four." };

            var (plan, rejection) = ChapterDiscoveryPlanner.FromAnnouncements(candidates, heard, "Chapter one.", Duration, null);

            Assert.Null(plan);
            Assert.Contains("do not read as one book's chapters", rejection!.Reason);
        }

        [Fact]
        public void FromAnnouncements_TakesTheEditionsTitlesWhenTheCountsAgree()
        {
            var candidates = new[] { Min(30), Min(60), Min(90) };
            var heard = new string?[] { "Chapter two.", "Chapter three.", "Chapter four." };
            var edition = new List<EmbeddedChapter>
            {
                new("Chapter 1: Dawn", TimeSpan.Zero, Min(31)),
                new("Chapter 2: Noon", Min(31), Min(61)),
                new("Chapter 3: Dusk", Min(61), Min(91)),
                new("Chapter 4: Night", Min(91), Duration)
            };

            var (plan, _) = ChapterDiscoveryPlanner.FromAnnouncements(candidates, heard, "Chapter one.", Duration, edition);

            Assert.Equal(["Chapter 1: Dawn", "Chapter 2: Noon", "Chapter 3: Dusk", "Chapter 4: Night"], plan!.Chapters.Select(c => c.Title));
            // The marks stay the file's own; the edition only lends names.
            Assert.Equal([TimeSpan.Zero, Min(30), Min(60), Min(90)], plan.Chapters.Select(c => c.Start));
            Assert.Contains("matches Audnexus, whose titles are used", plan.Note);
            Assert.False(plan.Partial);
        }

        [Fact]
        public void FromAnnouncements_IsPartialWhenFewerChaptersWereHeardThanTheEditionLists()
        {
            var candidates = new[] { Min(30), Min(60), Min(90) };
            var heard = new string?[] { "Chapter two.", "Chapter three.", "Chapter four." };
            var edition = Enumerable.Range(0, 8).Select(i => new EmbeddedChapter($"Chapter {i + 1}", Min(15 * i), Min(15 * (i + 1)))).ToList();

            var (plan, _) = ChapterDiscoveryPlanner.FromAnnouncements(candidates, heard, "Chapter one.", Duration, edition);

            Assert.True(plan!.Partial);
            Assert.Contains("Audnexus lists 8", plan.Note);
            Assert.Equal(["Chapter 1", "Chapter 2", "Chapter 3", "Chapter 4"], plan.Chapters.Select(c => c.Title));
        }

        [Fact]
        public void Weigh_CountsMatchingNumbersAsConfirmationAndOthersAsContradiction()
        {
            var (confirmed, contradicted) = ChapterDiscoveryPlanner.Weigh(
                ["Chapter one.", "the rain kept falling", "Chapter three.", "Prologue.", "Chapter two.", "Part two."]);

            // Chapter 1 at the first mark, Chapter 3 at the third, an unnumbered heading and a part: confirmations.
            // "Chapter two" at the fifth mark: the narrator's chapter two is not the edition's chapter five.
            Assert.Equal(4, confirmed);
            Assert.Equal(1, contradicted);
        }

        [Fact]
        public void FromEdition_UsesTheEditionsTitlesAndCountsTheNarratorsConfirmations()
        {
            var aligned = new AlignedEdition(
                TimeSpan.FromSeconds(12),
                [new EmbeddedChapter("Chapter 1: Dawn", TimeSpan.Zero, Min(30)), new EmbeddedChapter("Chapter 2: Noon", Min(30), Min(60)), new EmbeddedChapter("Chapter 3: Dusk", Min(60), Min(90))],
                Landed: 2,
                Snapped: [false, true, true]);

            var plan = ChapterDiscoveryPlanner.FromEdition(aligned, ["This is Audible.", "Chapter two.", "the rain kept falling"], Duration, "Book");

            Assert.Equal(ChapterSource.Audnexus, plan.Source);
            Assert.Equal(["Chapter 1: Dawn", "Chapter 2: Noon", "Chapter 3: Dusk"], plan.Chapters.Select(c => c.Title));
            Assert.Equal(Duration, plan.Chapters[^1].End);
            Assert.Contains("+0:12 offset", plan.Note);
            Assert.Contains("2 of them on the file's own pauses; 1 confirmed by the narrator", plan.Note);
            Assert.False(plan.Partial);
            Assert.Equal("Chapter two.", plan.Heard![1]);
        }

        [Fact]
        public void FromEdition_NamesAPlaceholderFromWhatTheNarratorSaid()
        {
            var aligned = new AlignedEdition(
                TimeSpan.Zero,
                [new EmbeddedChapter("Track 1", TimeSpan.Zero, Min(30)), new EmbeddedChapter("Track 2", Min(30), Min(60))],
                Landed: 1,
                Snapped: [false, true]);

            var plan = ChapterDiscoveryPlanner.FromEdition(aligned, [null, "Chapter two. The drop."], Duration, "Book");

            Assert.Equal(["Track 1", "Chapter 2: The drop"], plan.Chapters.Select(c => c.Title));
            Assert.Contains("no offset", plan.Note);
        }

        [Fact]
        public void FromEdition_IsPartialWhenNoMarkSitsOnAPause()
        {
            var aligned = new AlignedEdition(
                TimeSpan.Zero,
                [new EmbeddedChapter("Chapter 1", TimeSpan.Zero, Min(30)), new EmbeddedChapter("Chapter 2", Min(30), Min(60))],
                Landed: 0,
                Snapped: [false, false]);

            var plan = ChapterDiscoveryPlanner.FromEdition(aligned, null, Duration, "Book");

            Assert.True(plan.Partial);
            Assert.Contains("none of them on the file's own pauses", plan.Note);
            Assert.Null(plan.Heard);
        }
    }
}
