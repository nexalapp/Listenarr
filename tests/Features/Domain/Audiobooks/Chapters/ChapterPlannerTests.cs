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
    [Trait("Name", "ChapterPlannerTests")]
    [Trait("Category", "Domain")]
    public sealed class ChapterPlannerTests : BaseTests
    {
        private static readonly TimeSpan Duration = TimeSpan.FromMinutes(100);

        private static List<EmbeddedChapter> Evenly(int count, TimeSpan each, TimeSpan? from = null)
        {
            var start = from ?? TimeSpan.Zero;
            var chapters = new List<EmbeddedChapter>(count);
            for (var i = 0; i < count; i++)
            {
                chapters.Add(new EmbeddedChapter($"Chapter {i + 1}", start + each * i, start + each * (i + 1)));
            }

            return chapters;
        }

        [Fact]
        public void Plan_PrefersWhatTheFilePlays()
        {
            var played = Evenly(10, TimeSpan.FromMinutes(10));
            var audnexus = (Evenly(12, TimeSpan.FromMinutes(8)), Duration);

            var (plan, rejection) = ChapterPlanner.Plan(played, Duration, audnexus, recovered: null);

            Assert.Null(rejection);
            Assert.Equal(ChapterSource.Played, plan!.Source);
            Assert.Equal(10, plan.Chapters.Count);
            Assert.False(plan.Partial);
        }

        [Fact]
        public void Plan_FallsBackToAudnexusWhenTheRuntimeMatches()
        {
            var audnexus = (Evenly(12, TimeSpan.FromMinutes(8)), Duration + TimeSpan.FromSeconds(20));

            var (plan, _) = ChapterPlanner.Plan(played: [], Duration, audnexus, recovered: null);

            Assert.Equal(ChapterSource.Audnexus, plan!.Source);
            Assert.Equal(12, plan.Chapters.Count);
        }

        [Fact]
        public void Plan_RefusesAudnexusForADifferentEdition()
        {
            // Nine minutes off on a hundred-minute book is an abridgement, not rounding.
            var audnexus = (Evenly(12, TimeSpan.FromMinutes(8)), Duration - TimeSpan.FromMinutes(9));

            var (plan, rejection) = ChapterPlanner.Plan(played: [], Duration, audnexus, recovered: null);

            Assert.Null(plan);
            Assert.NotNull(rejection);
        }

        [Fact]
        public void Plan_RecoveredAtomIsPartialAndGetsAnOpeningChapter()
        {
            // The first two entries were lost with the header; the run starts at minute 20.
            var recovered = Evenly(8, TimeSpan.FromMinutes(10), from: TimeSpan.FromMinutes(20));

            var (plan, _) = ChapterPlanner.Plan(played: null, Duration, audnexus: null, recovered);

            Assert.Equal(ChapterSource.RecoveredAtom, plan!.Source);
            Assert.True(plan.Partial);
            Assert.Equal(9, plan.Chapters.Count);
            Assert.Equal(TimeSpan.Zero, plan.Chapters[0].Start);
            Assert.Equal("Chapter 1", plan.Chapters[0].Title);
            Assert.Equal(TimeSpan.FromMinutes(20), plan.Chapters[0].End);
        }

        [Fact]
        public void Plan_RejectsWhenNothingIsUsable()
        {
            var (plan, rejection) = ChapterPlanner.Plan(played: [], Duration, audnexus: null, recovered: null);

            Assert.Null(plan);
            Assert.Contains("no readable chapter marks", rejection!.Reason);
        }

        [Fact]
        public void Plan_RejectsAPlayedListThatIsOutOfOrder()
        {
            var played = new List<EmbeddedChapter>
            {
                new("Two", TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(60)),
                new("One", TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(40))
            };

            var (plan, rejection) = ChapterPlanner.Plan(played, Duration, audnexus: null, recovered: null);

            Assert.Null(plan);
            Assert.Contains("not usable", rejection!.Reason);
        }

        [Fact]
        public void Plan_NormalisesEndsAndTitles()
        {
            var played = new List<EmbeddedChapter>
            {
                new(null, TimeSpan.Zero, TimeSpan.FromMinutes(5)),          // ends are recomputed
                new("  Two  ", TimeSpan.FromMinutes(30), TimeSpan.Zero),
                new("Tail", Duration - TimeSpan.FromSeconds(1), Duration)  // a mark at the very end is dropped
            };

            var (plan, _) = ChapterPlanner.Plan(played, Duration, audnexus: null, recovered: null);

            Assert.Equal(2, plan!.Chapters.Count);
            Assert.Equal("Chapter 1", plan.Chapters[0].Title);
            Assert.Equal(TimeSpan.FromMinutes(30), plan.Chapters[0].End);
            Assert.Equal("Two", plan.Chapters[1].Title);
            Assert.Equal(Duration, plan.Chapters[1].End);
        }
    }
}
