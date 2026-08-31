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

namespace Listenarr.Tests.Features.Domain.Audiobooks.Conversion
{
    [Trait("Name", "ConversionPlannerTests")]
    [Trait("Category", "Domain")]
    public sealed class ConversionPlannerTests : BaseTests
    {
        private static ConversionSource Source(
            string name,
            double seconds = 60,
            int? bitRate = 64_000,
            int? sampleRate = 44_100,
            int? channels = 2,
            string? embeddedTitle = null,
            string? relativePath = null) =>
            new(
                $"/library/book/{name}",
                relativePath ?? name,
                TimeSpan.FromSeconds(seconds),
                bitRate,
                sampleRate,
                channels,
                embeddedTitle);

        // ---- ordering ---------------------------------------------------------------

        [Fact]
        public void BuildPlan_OrdersNumericallyNotLexically()
        {
            var plan = ConversionPlanner.BuildPlan(
                [
                    Source("Chapter 10.mp3"),
                    Source("Chapter 2.mp3"),
                    Source("Chapter 1.mp3"),
                ],
                StringComparer.Ordinal);

            Assert.Equal(
                ["Chapter 1.mp3", "Chapter 2.mp3", "Chapter 10.mp3"],
                plan.OrderedSources.Select(s => Path.GetFileName(s.FullPath)));
        }

        [Fact]
        public void BuildPlan_OrdersAcrossDiscDirectories()
        {
            var plan = ConversionPlanner.BuildPlan(
                [
                    Source("d2t1.mp3", relativePath: Path.Combine("Disc 2", "Track 1.mp3")),
                    Source("d1t2.mp3", relativePath: Path.Combine("Disc 1", "Track 2.mp3")),
                    Source("d1t10.mp3", relativePath: Path.Combine("Disc 1", "Track 10.mp3")),
                ],
                StringComparer.Ordinal);

            Assert.Equal(
                ["d1t2.mp3", "d1t10.mp3", "d2t1.mp3"],
                plan.OrderedSources.Select(s => Path.GetFileName(s.FullPath)));
        }

        [Fact]
        public void BuildPlan_DeduplicatesRepeatedSourcePaths()
        {
            var plan = ConversionPlanner.BuildPlan(
                [Source("Chapter 1.mp3"), Source("Chapter 1.mp3")],
                StringComparer.Ordinal);

            Assert.Single(plan.OrderedSources);
            Assert.Single(plan.Chapters);
        }

        // ---- chapter generation -----------------------------------------------------

        [Fact]
        public void BuildPlan_ChapterBoundariesAccumulateWithoutGapOrOverlap()
        {
            var plan = ConversionPlanner.BuildPlan(
                [
                    Source("Chapter 1.mp3", seconds: 30),
                    Source("Chapter 2.mp3", seconds: 45),
                    Source("Chapter 3.mp3", seconds: 15),
                ],
                StringComparer.Ordinal);

            Assert.Equal(TimeSpan.Zero, plan.Chapters[0].Start);
            Assert.Equal(TimeSpan.FromSeconds(30), plan.Chapters[0].End);
            Assert.Equal(TimeSpan.FromSeconds(30), plan.Chapters[1].Start);
            Assert.Equal(TimeSpan.FromSeconds(75), plan.Chapters[1].End);
            Assert.Equal(TimeSpan.FromSeconds(75), plan.Chapters[2].Start);
            Assert.Equal(TimeSpan.FromSeconds(90), plan.Chapters[2].End);
            Assert.Equal(TimeSpan.FromSeconds(90), plan.TotalDuration);
        }

        [Fact]
        public void BuildPlan_NumbersChaptersFromOneInPlayOrder()
        {
            var plan = ConversionPlanner.BuildPlan(
                [Source("Chapter 10.mp3"), Source("Chapter 1.mp3")],
                StringComparer.Ordinal);

            Assert.Equal([1, 2], plan.Chapters.Select(c => c.Number));
            Assert.Equal("Chapter 1.mp3", Path.GetFileName(plan.Chapters[0].SourceFullPath));
        }

        [Fact]
        public void BuildPlan_PrefersEmbeddedTitleForChapterName()
        {
            var plan = ConversionPlanner.BuildPlan(
                [Source("01.mp3", embeddedTitle: "An Unexpected Party")],
                StringComparer.Ordinal);

            Assert.Equal("An Unexpected Party", plan.Chapters[0].Title);
        }

        [Fact]
        public void BuildPlan_IgnoresEmbeddedTitleThatIsOnlyANumber()
        {
            var plan = ConversionPlanner.BuildPlan(
                [Source("A Long Expected Party.mp3", embeddedTitle: "01")],
                StringComparer.Ordinal);

            Assert.Equal("A Long Expected Party", plan.Chapters[0].Title);
        }

        [Fact]
        public void BuildPlan_StripsLeadingTrackNumberFromFilenameTitle()
        {
            var plan = ConversionPlanner.BuildPlan(
                [Source("03 - Riddles in the Dark.mp3")],
                StringComparer.Ordinal);

            Assert.Equal("Riddles in the Dark", plan.Chapters[0].Title);
        }

        [Fact]
        public void BuildPlan_FallsBackToChapterNumber_WhenNameCarriesNoWords()
        {
            var plan = ConversionPlanner.BuildPlan(
                [Source("01.mp3"), Source("02.mp3")],
                StringComparer.Ordinal);

            Assert.Equal(["Chapter 1", "Chapter 2"], plan.Chapters.Select(c => c.Title));
        }

        [Fact]
        public void BuildPlan_KeepsChapterUsable_WhenSourceDurationIsUnknown()
        {
            var plan = ConversionPlanner.BuildPlan(
                [
                    Source("Chapter 1.mp3", seconds: 0),
                    Source("Chapter 2.mp3", seconds: 30),
                ],
                StringComparer.Ordinal);

            Assert.Equal(TimeSpan.Zero, plan.Chapters[0].Start);
            Assert.Equal(TimeSpan.Zero, plan.Chapters[0].End);
            Assert.Equal(TimeSpan.Zero, plan.Chapters[1].Start);
            Assert.Equal(TimeSpan.FromSeconds(30), plan.Chapters[1].End);
        }

        [Fact]
        public void BuildPlan_IgnoresAnEmbeddedTitleSharedByEveryPart()
        {
            // Parts split from one book commonly all carry the book's own title tag.
            // Preferring it would name every chapter identically, which is worse than
            // the filenames — those at least tell the parts apart.
            var plan = ConversionPlanner.BuildPlan(
                [
                    Source("Part 1.mp3", embeddedTitle: "Building Bridges"),
                    Source("Part 2.mp3", embeddedTitle: "Building Bridges"),
                    Source("Part 10.mp3", embeddedTitle: "Building Bridges"),
                ],
                StringComparer.Ordinal);

            Assert.Equal(["Part 1", "Part 2", "Part 10"], plan.Chapters.Select(c => c.Title));
        }

        [Fact]
        public void BuildPlan_KeepsEmbeddedTitlesThatActuallyDifferPerPart()
        {
            var plan = ConversionPlanner.BuildPlan(
                [
                    Source("01.mp3", embeddedTitle: "An Unexpected Party"),
                    Source("02.mp3", embeddedTitle: "Roast Mutton"),
                ],
                StringComparer.Ordinal);

            Assert.Equal(["An Unexpected Party", "Roast Mutton"], plan.Chapters.Select(c => c.Title));
        }

        [Fact]
        public void BuildPlan_KeepsASingleFilesEmbeddedTitle()
        {
            // One source cannot collide with anything, so its own title still stands.
            var plan = ConversionPlanner.BuildPlan(
                [Source("01.mp3", embeddedTitle: "Building Bridges")],
                StringComparer.Ordinal);

            Assert.Equal("Building Bridges", plan.Chapters[0].Title);
        }

        // ---- files that already carry chapters --------------------------------------

        [Fact]
        public void BuildPlan_KeepsChaptersOfAnAlreadyMergedFile()
        {
            // A book previously merged into one chaptered MP3. Treating it as a single
            // chapter would discard every mark it has, and that merge is exactly why
            // someone would convert it to M4B.
            var merged = Source("Whole Book.mp3", seconds: 90) with
            {
                EmbeddedChapters =
                [
                    new EmbeddedChapter("Opening", TimeSpan.Zero, TimeSpan.FromSeconds(30)),
                    new EmbeddedChapter("Middle", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)),
                    new EmbeddedChapter("End", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(90)),
                ]
            };

            var plan = ConversionPlanner.BuildPlan([merged], StringComparer.Ordinal);

            Assert.Equal(["Opening", "Middle", "End"], plan.Chapters.Select(c => c.Title));
            Assert.Equal([1, 2, 3], plan.Chapters.Select(c => c.Number));
            Assert.Equal(TimeSpan.FromSeconds(30), plan.Chapters[1].Start);
            Assert.Equal(TimeSpan.FromSeconds(90), plan.TotalDuration);
        }

        [Fact]
        public void BuildPlan_OffsetsEmbeddedChaptersByThePrecedingFiles()
        {
            var first = Source("01.mp3", seconds: 20);
            var second = Source("02.mp3", seconds: 60) with
            {
                EmbeddedChapters =
                [
                    new EmbeddedChapter("Part A", TimeSpan.Zero, TimeSpan.FromSeconds(30)),
                    new EmbeddedChapter("Part B", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)),
                ]
            };

            var plan = ConversionPlanner.BuildPlan([first, second], StringComparer.Ordinal);

            Assert.Equal(3, plan.Chapters.Count);
            // The second file's own marks are relative to itself and have to shift by the
            // 20 seconds that play before it.
            Assert.Equal(TimeSpan.FromSeconds(20), plan.Chapters[1].Start);
            Assert.Equal("Part A", plan.Chapters[1].Title);
            Assert.Equal(TimeSpan.FromSeconds(50), plan.Chapters[2].Start);
            Assert.Equal(TimeSpan.FromSeconds(80), plan.TotalDuration);
        }

        [Fact]
        public void BuildPlan_IgnoresASingleChapterSpanningTheWholeFile()
        {
            // Some tools write one mark covering everything, which says no more than
            // one-chapter-per-file already does and has a worse title.
            var source = Source("A Long Expected Party.mp3", seconds: 60) with
            {
                EmbeddedChapters =
                [
                    new EmbeddedChapter("track 1", TimeSpan.Zero, TimeSpan.FromSeconds(60)),
                ]
            };

            var plan = ConversionPlanner.BuildPlan([source], StringComparer.Ordinal);

            Assert.Single(plan.Chapters);
            Assert.Equal("A Long Expected Party", plan.Chapters[0].Title);
        }

        [Fact]
        public void BuildPlan_OrdersAndClampsUntrustworthyEmbeddedChapters()
        {
            // The marks come from whatever tool wrote the file: they can arrive unsorted,
            // run past the end of the audio, or be empty. A mark past the end would land
            // in the next file's audio once offset.
            var source = Source("Whole Book.mp3", seconds: 60) with
            {
                EmbeddedChapters =
                [
                    new EmbeddedChapter("Second", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120)),
                    new EmbeddedChapter("First", TimeSpan.Zero, TimeSpan.FromSeconds(30)),
                    new EmbeddedChapter("Empty", TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(45)),
                ]
            };

            var plan = ConversionPlanner.BuildPlan([source], StringComparer.Ordinal);

            Assert.Equal(["First", "Second"], plan.Chapters.Select(c => c.Title));
            Assert.Equal(TimeSpan.FromSeconds(60), plan.Chapters[1].End);
        }

        [Fact]
        public void BuildPlan_NamesAnUntitledEmbeddedChapterByItsNumber()
        {
            var source = Source("Whole Book.mp3", seconds: 60) with
            {
                EmbeddedChapters =
                [
                    new EmbeddedChapter(null, TimeSpan.Zero, TimeSpan.FromSeconds(30)),
                    new EmbeddedChapter("  ", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)),
                ]
            };

            var plan = ConversionPlanner.BuildPlan([source], StringComparer.Ordinal);

            Assert.Equal(["Chapter 1", "Chapter 2"], plan.Chapters.Select(c => c.Title));
        }

        // ---- encode parameters ------------------------------------------------------

        [Fact]
        public void BuildPlan_MatchesTheHighestSourceBitrate()
        {
            var plan = ConversionPlanner.BuildPlan(
                [
                    Source("Chapter 1.mp3", bitRate: 32_000),
                    Source("Chapter 2.mp3", bitRate: 96_000),
                ],
                StringComparer.Ordinal);

            Assert.Equal(96_000, plan.TargetBitRate);
        }

        [Fact]
        public void BuildPlan_CapsBitrateAt128k()
        {
            var plan = ConversionPlanner.BuildPlan(
                [Source("Chapter 1.mp3", bitRate: 320_000)],
                StringComparer.Ordinal);

            Assert.Equal(ConversionPlanner.MaximumBitRate, plan.TargetBitRate);
        }

        [Fact]
        public void BuildPlan_FloorsBitrate_WhenSourceRateIsUnknown()
        {
            var plan = ConversionPlanner.BuildPlan(
                [Source("Chapter 1.mp3", bitRate: null)],
                StringComparer.Ordinal);

            Assert.Equal(ConversionPlanner.MinimumBitRate, plan.TargetBitRate);
        }

        [Fact]
        public void BuildPlan_NormalisesToTheStrongestStreamParameters()
        {
            // The concat demuxer would silently adopt the first file's 22kHz mono and
            // downmix the second. The plan has to name the stronger target instead.
            var plan = ConversionPlanner.BuildPlan(
                [
                    Source("01.mp3", sampleRate: 22_050, channels: 1),
                    Source("02.mp3", sampleRate: 44_100, channels: 2),
                ],
                StringComparer.Ordinal);

            Assert.Equal(44_100, plan.TargetSampleRate);
            Assert.Equal(2, plan.TargetChannels);
        }

        [Fact]
        public void BuildPlan_DoesNotExceedStereo()
        {
            var plan = ConversionPlanner.BuildPlan(
                [Source("01.mp3", channels: 6)],
                StringComparer.Ordinal);

            Assert.Equal(2, plan.TargetChannels);
        }

        // ---- failure path -----------------------------------------------------------

        [Fact]
        public void BuildPlan_RejectsAnEmptySourceSet()
        {
            Assert.Throws<ArgumentException>(() =>
                ConversionPlanner.BuildPlan([], StringComparer.Ordinal));
        }

        [Fact]
        public void BuildPlan_RejectsSourcesWithNoUsablePath()
        {
            Assert.Throws<ArgumentException>(() =>
                ConversionPlanner.BuildPlan(
                    [new ConversionSource("  ", null, TimeSpan.FromSeconds(1), 64_000, 44_100, 2)],
                    StringComparer.Ordinal));
        }
    }
}
