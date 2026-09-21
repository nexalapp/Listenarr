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
    [Trait("Name", "SilenceCandidatesTests")]
    [Trait("Category", "Domain")]
    public sealed class SilenceCandidatesTests : BaseTests
    {
        private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

        private static SilenceSpan At(double minutes, double seconds) =>
            new(TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds));

        [Fact]
        public void Select_PrefersTheLongPausesAndMarksJustBeforeSpeechResumes()
        {
            var silences = new[] { At(10, 2.5), At(20, 0.8), At(30, 3), At(40, 1.2), At(45, 2.2), At(50, 2) };

            var marks = SilenceCandidates.Select(silences, Hour, wanted: null, cap: 400);

            Assert.Equal(4, marks.Count);
            Assert.Equal(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(2.5) - SilenceCandidates.Lead, marks[0]);
            Assert.Equal(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(3) - SilenceCandidates.Lead, marks[1]);
            Assert.Equal(TimeSpan.FromMinutes(45) + TimeSpan.FromSeconds(2.2) - SilenceCandidates.Lead, marks[2]);
            Assert.Equal(TimeSpan.FromMinutes(50) + TimeSpan.FromSeconds(2) - SilenceCandidates.Lead, marks[3]);
        }

        [Fact]
        public void Select_LetsTheShortPausesInWhenTheLongOnesAreTooFewForTheEdition()
        {
            var silences = new[] { At(10, 2.5), At(20, 1), At(30, 1.1), At(40, 1.2), At(50, 2), At(55, 0.5) };

            var marks = SilenceCandidates.Select(silences, Hour, wanted: 5, cap: 400);

            // The five of at least a second; the half-second one stays out.
            Assert.Equal(5, marks.Count);
        }

        [Fact]
        public void Select_LetsTheShortPausesInWhenTheLongOnesAreFewerThanAnyBookHas()
        {
            var silences = new[] { At(10, 2.5), At(20, 1), At(30, 1.1), At(40, 1.2), At(50, 2) };

            var marks = SilenceCandidates.Select(silences, Hour, wanted: null, cap: 400);

            Assert.Equal(5, marks.Count);
        }

        [Fact]
        public void Select_IgnoresThePaddingAtEitherEnd()
        {
            var silences = new[] { new SilenceSpan(TimeSpan.Zero, TimeSpan.FromSeconds(3)), At(10, 3), At(20, 3), At(30, 3), At(40, 3), new SilenceSpan(Hour - TimeSpan.FromSeconds(4), Hour) };

            var marks = SilenceCandidates.Select(silences, Hour, wanted: null, cap: 400);

            Assert.Equal(4, marks.Count);
            Assert.All(marks, mark => Assert.InRange(mark, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(41)));
        }

        [Fact]
        public void Select_KeepsTheLongerOfTwoPausesABreathApart()
        {
            // An announcement between two pauses: "…prose. [2.0s] Chapter Four. [3.0s] The morning…"
            var silences = new[] { At(10, 2), new SilenceSpan(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(4), TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(7)), At(20, 3), At(30, 3), At(40, 3) };

            var marks = SilenceCandidates.Select(silences, Hour, wanted: null, cap: 400);

            Assert.Equal(4, marks.Count);
            Assert.Equal(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(7) - SilenceCandidates.Lead, marks[0]);
        }

        [Fact]
        public void Select_KeepsTheFirstOfTwoEqualPausesABreathApart()
        {
            var silences = new[] { At(10, 3), new SilenceSpan(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(8)), At(20, 3), At(30, 3), At(40, 3) };

            var marks = SilenceCandidates.Select(silences, Hour, wanted: null, cap: 400);

            Assert.Equal(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(3) - SilenceCandidates.Lead, marks[0]);
        }

        [Fact]
        public void Select_CapsAtTheLongestPausesInOrder()
        {
            var silences = Enumerable.Range(1, 50).Select(i => At(i, 2 + (i % 7) / 10.0)).ToList();

            var marks = SilenceCandidates.Select(silences, Hour, wanted: null, cap: 10);

            Assert.Equal(10, marks.Count);
            Assert.Equal(marks.OrderBy(m => m), marks);
            // The kept ones are the longest: the seven 2.6s pauses and three of the 2.5s ones.
            Assert.All(marks, mark => Assert.InRange((int)Math.Round(mark.TotalMinutes) % 7, 5, 6));
        }

        [Fact]
        public void Select_LetsTheFilesOwnMarksStandInForNearbyPausesAndJoinTheRest()
        {
            var silences = new[] { At(10, 3), At(20, 3), At(30, 3), At(40, 3) };
            // A rip's marks: one a breath before the 20-minute pause, one where there is no pause at all.
            var marks = new[] { TimeSpan.Zero, Min(20) + TimeSpan.FromSeconds(1), Min(25) };

            var candidates = SilenceCandidates.Select(silences, Hour, wanted: null, cap: 400, marks);

            Assert.Equal(5, candidates.Count);
            Assert.Equal(Min(20) + TimeSpan.FromSeconds(1), candidates[1]);
            Assert.Equal(Min(25), candidates[2]);
            Assert.DoesNotContain(TimeSpan.Zero, candidates);
        }

        private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

        [Fact]
        public void Select_ReturnsNothingForAFileWithoutPauses()
        {
            Assert.Empty(SilenceCandidates.Select([], Hour, null, 400));
            Assert.Empty(SilenceCandidates.Select([At(10, 0.5), At(20, 0.9)], Hour, null, 400));
        }
    }
}
