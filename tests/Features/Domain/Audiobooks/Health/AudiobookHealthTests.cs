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
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Health;
using Listenarr.Domain.Audiobooks.Quality;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks.Health
{
    [Trait("Name", "AudiobookHealthTests")]
    [Trait("Category", "Domain")]
    public sealed class AudiobookHealthTests : BaseTests
    {
        /// <summary>A book with nothing wrong anywhere.</summary>
        private static AudiobookHealthFacts Sound(
            AudioAuditVerdict audit = AudioAuditVerdict.Match,
            bool accepted = false,
            bool? tagsClean = true,
            bool? namingClean = true,
            ChapterHealth chapters = ChapterHealth.Healthy,
            AudioQuality quality = AudioQuality.Clean) =>
            new(audit, accepted, tagsClean, null, namingClean, null, chapters, quality, null);

        [Fact]
        public void EverythingSound_ScoresFull()
        {
            var report = AudiobookHealth.Judge(Sound());

            Assert.Equal(100, report.Score);
            Assert.All(
                new[] { report.Tags, report.Naming, report.Chapters, report.Audio },
                category => Assert.Equal(HealthState.Good, category.State));
        }

        [Fact]
        public void NothingKnown_HasNoScoreAtAll()
        {
            var facts = new AudiobookHealthFacts(
                AudioAuditVerdict.NotAudited, false, null, null, null, null,
                ChapterHealth.Unknown, AudioQuality.Unknown, null);

            var report = AudiobookHealth.Judge(facts);

            Assert.Null(report.Score);
        }

        /// <summary>A book is scored on the checks that ran, not punished for the ones that have not.</summary>
        [Fact]
        public void OneKnownCategory_IsScoredOnItAlone()
        {
            var facts = new AudiobookHealthFacts(
                AudioAuditVerdict.NotAudited, false, null, null, true, null,
                ChapterHealth.Unknown, AudioQuality.Unknown, null);

            var report = AudiobookHealth.Judge(facts);

            Assert.Equal(100, report.Score);
            Assert.Equal(HealthState.Unknown, report.Tags.State);
        }

        /// <summary>
        /// A file perfectly tagged with the wrong book's metadata is not correct: the
        /// audit's verdict is part of tag correctness, which is where "depends on
        /// transcriptions" lives.
        /// </summary>
        [Fact]
        public void WrongBookHeard_MakesTagsBad_WhateverTheFilesCarry()
        {
            var report = AudiobookHealth.Judge(Sound(audit: AudioAuditVerdict.Mismatch, tagsClean: true));

            Assert.Equal(HealthState.Bad, report.Tags.State);
        }

        /// <summary>Someone listened and vouched for the record; the verdict stops counting.</summary>
        [Fact]
        public void AcceptedVerdict_CountsAsCorrect()
        {
            var report = AudiobookHealth.Judge(Sound(audit: AudioAuditVerdict.Mismatch, accepted: true));

            Assert.Equal(HealthState.Good, report.Tags.State);
            Assert.Equal(100, report.Score);
        }

        [Fact]
        public void NarratorMismatch_WarnsTags()
        {
            var report = AudiobookHealth.Judge(Sound(audit: AudioAuditVerdict.NarratorMismatch));

            Assert.Equal(HealthState.Warn, report.Tags.State);
            Assert.Equal(90, report.Score);
        }

        [Fact]
        public void PendingTagWrites_WarnTags()
        {
            var report = AudiobookHealth.Judge(Sound(tagsClean: false));

            Assert.Equal(HealthState.Warn, report.Tags.State);
        }

        [Fact]
        public void PendingRename_WarnsNaming()
        {
            var report = AudiobookHealth.Judge(Sound(namingClean: false));

            Assert.Equal(HealthState.Warn, report.Naming.State);
            Assert.Equal(90, report.Score);
        }

        [Fact]
        public void CorruptChapters_AreBad_PlaceholderTitlesAWarn()
        {
            Assert.Equal(HealthState.Bad, AudiobookHealth.Judge(Sound(chapters: ChapterHealth.Corrupt)).Chapters.State);
            Assert.Equal(HealthState.Warn, AudiobookHealth.Judge(Sound(chapters: ChapterHealth.GenericTitles)).Chapters.State);
            Assert.Equal(HealthState.Unknown, AudiobookHealth.Judge(Sound(chapters: ChapterHealth.Unknown)).Chapters.State);
        }

        [Fact]
        public void AudioDefects_GradeBySeverity_AndProductionAudioIsNotADefect()
        {
            Assert.Equal(HealthState.Warn, AudiobookHealth.Judge(Sound(quality: AudioQuality.Hiss)).Audio.State);
            Assert.Equal(HealthState.Bad, AudiobookHealth.Judge(Sound(quality: AudioQuality.CodecDamaged)).Audio.State);
            Assert.Equal(HealthState.Good, AudiobookHealth.Judge(Sound(quality: AudioQuality.ProductionAudio)).Audio.State);
        }

        /// <summary>The worst book this score can describe: wrong record, wrong names, broken marks, damaged audio.</summary>
        [Fact]
        public void EverythingWrong_ScoresZero()
        {
            var facts = new AudiobookHealthFacts(
                AudioAuditVerdict.Mismatch, false, false, null, false, null,
                ChapterHealth.Corrupt, AudioQuality.CodecDamaged, null);

            var report = AudiobookHealth.Judge(facts);

            // Tags bad, chapters bad, audio bad; naming warn.
            Assert.Equal(15, report.Score);
        }
    }
}
