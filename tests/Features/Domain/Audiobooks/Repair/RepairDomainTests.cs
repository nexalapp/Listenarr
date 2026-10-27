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
using Listenarr.Domain.Audiobooks.Repair;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks.Repair
{
    [Trait("Name", "RepairDomainTests")]
    [Trait("Category", "Domain")]
    public sealed class RepairDomainTests : BaseTests
    {
        private static ConversionSource Source(
            string path = "/library/book/a.mp3",
            double minutes = 60,
            int? bitRate = 64_000,
            int? sampleRate = 22_050,
            int? channels = 1) =>
            new(path, Path.GetFileName(path), TimeSpan.FromMinutes(minutes), bitRate, sampleRate, channels);

        /// <summary>
        /// The measured trap this policy exists for: a restored book encoded back at its
        /// source's rate lost the whole repair at the encode step. The repair target must
        /// differ from the transcode policy's answer on exactly such a source.
        /// </summary>
        [Fact]
        public void RepairTarget_IsFixed_AndNotTheTranscodePolicysAnswer()
        {
            var sources = new[] { Source(bitRate: 48_000, sampleRate: 22_050) };

            var repair = RepairPlanPolicy.SelectTarget(sources);
            var transcode = ConversionPlanner.SelectDefaultTarget(sources);

            Assert.Equal(128_000, repair.BitRate);
            Assert.Equal(44_100, repair.SampleRate);
            Assert.NotEqual(transcode.SampleRate, repair.SampleRate);
            Assert.NotEqual(transcode.BitRate, repair.BitRate);
        }

        [Fact]
        public void RepairTarget_ChannelsStillFollowTheSource_CappedAtStereo()
        {
            Assert.Equal(1, RepairPlanPolicy.SelectTarget([Source(channels: 1)]).Channels);
            Assert.Equal(2, RepairPlanPolicy.SelectTarget([Source(channels: 6)]).Channels);
        }

        [Fact]
        public void PlannerOverload_SelectorChangesOnlyTheTarget()
        {
            var sources = new[] { Source("/library/book/01.mp3", 30), Source("/library/book/02.mp3", 40) };

            var transcode = ConversionPlanner.BuildPlan(sources, StringComparer.Ordinal);
            var repair = ConversionPlanner.BuildPlan(sources, StringComparer.Ordinal, RepairPlanPolicy.SelectTarget);

            Assert.Equal(
                transcode.Chapters.Select(chapter => (chapter.Title, chapter.Start, chapter.End)),
                repair.Chapters.Select(chapter => (chapter.Title, chapter.Start, chapter.End)));
            Assert.Equal(
                transcode.OrderedSources.Select(source => source.FullPath),
                repair.OrderedSources.Select(source => source.FullPath));
            Assert.Equal(44_100, repair.TargetSampleRate);
            Assert.Equal(22_050, transcode.TargetSampleRate);
        }

        [Fact]
        public void ChunkGrid_ButtsExactly_AndAbsorbsAShortTail()
        {
            // 50 minutes: two 20-minute chunks and a 10-minute remainder.
            var chunks = RepairChunkPlanner.Plan(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(20));
            Assert.Equal(3, chunks.Count);
            Assert.Equal(TimeSpan.Zero, chunks[0].Start);
            for (var i = 1; i < chunks.Count; i++)
            {
                Assert.Equal(chunks[i - 1].Start + chunks[i - 1].Length, chunks[i].Start);
            }

            Assert.Equal(TimeSpan.FromMinutes(50), chunks[^1].Start + chunks[^1].Length);

            // 40 minutes and 10 seconds: the 10-second tail would be all seam and no
            // audio, so the second chunk absorbs it.
            var absorbed = RepairChunkPlanner.Plan(TimeSpan.FromMinutes(40) + TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(20));
            Assert.Equal(2, absorbed.Count);
            Assert.Equal(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(10), absorbed[^1].Length);
        }

        [Fact]
        public void ChunkGrid_IsFiveMinutes_SoARestartCostsLittle()
        {
            var chunks = RepairChunkPlanner.Plan(TimeSpan.FromMinutes(12.5));

            Assert.Equal([TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2.5)], chunks.Select(c => c.Length));
            // Manifests from before the change were all cut at twenty.
            Assert.Equal(TimeSpan.FromMinutes(20), RepairChunkPlanner.LegacyChunkLength);
        }

        [Fact]
        public void ChunkGrid_ShortFileIsOneChunk_EmptyFileIsNone()
        {
            Assert.Single(RepairChunkPlanner.Plan(TimeSpan.FromMinutes(5)));
            Assert.Empty(RepairChunkPlanner.Plan(TimeSpan.Zero));
        }

        /// <summary>
        /// The music guard's truth table. Removing something loud is the model destroying
        /// a score; removing something quiet is cleaning, which is the whole point.
        /// </summary>
        [Theory]
        [InlineData(-20, -30, ChunkOutcome.UseOriginal)] // loud passage, big drop: a score was deleted
        [InlineData(-20, -22, ChunkOutcome.UseEnhanced)] // loud passage, mild change: cleaning
        [InlineData(-50, -70, ChunkOutcome.UseEnhanced)] // quiet passage, big drop: hiss removed from silence
        [InlineData(-34, -41, ChunkOutcome.UseOriginal)] // just over both lines
        public void MusicGuard_SplicesOnlyWhenALoudPassageLostSeriousLevel(
            double originalDb,
            double enhancedDb,
            ChunkOutcome expected)
        {
            Assert.Equal(expected, ChunkEnergyGuard.Decide(originalDb, enhancedDb));
        }

        /// <summary>
        /// Repairable is wider than convertible by exactly the M4B: a damaged book
        /// already merged is just as repairable as the MP3s it was merged from.
        /// </summary>
        [Fact]
        public void RepairableSource_AcceptsTheM4bThatConvertibleRefuses()
        {
            Assert.True(RepairableSource.IsRepairable("/library/book/book.m4b"));
            Assert.False(ConvertibleSource.IsConvertible("/library/book/book.m4b"));
            Assert.True(RepairableSource.IsRepairable("/library/book/01.mp3"));
            Assert.False(RepairableSource.IsRepairable("/library/book/cover.jpg"));
            Assert.False(RepairableSource.IsRepairable(null));
        }
    }
}
