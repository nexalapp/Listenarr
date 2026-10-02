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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks
{
    [Trait("Name", "AudioAuditFileIdentityTests")]
    [Trait("Category", "Domain")]
    public sealed class AudioAuditFileIdentityTests : BaseTests
    {
        private static TimeSpan Mins(double minutes) => TimeSpan.FromMinutes(minutes);

        [Fact]
        [Trait("Method", "Matches")]
        [Trait("Scenario", "TheSameAudioUnchanged")]
        public void Matches_TheSameAudioUnchanged()
        {
            var taken = AudioAuditFileIdentity.Of([Mins(774), Mins(136)], "medium.en");
            var now = AudioAuditFileIdentity.Of([Mins(774), Mins(136)], "medium.en");

            Assert.True(AudioAuditFileIdentity.Matches(taken, now));
        }

        /// <summary>
        /// The whole point of the change: a tag write rewrites the container and leaves the
        /// audio alone, so a corrected narrator must not cost a re-listen. Under size and
        /// modification time it cost one every time, and lapsed the acceptance with it.
        /// </summary>
        [Fact]
        [Trait("Method", "Matches")]
        [Trait("Scenario", "ATagWriteIsNotANewRecording")]
        public void Matches_SurvivesARewrittenContainer()
        {
            // Same audio, same duration. Nothing in the identity can see the rewrite.
            var before = AudioAuditFileIdentity.Of([TimeSpan.FromSeconds(46442.119667)], "medium.en");
            var after = AudioAuditFileIdentity.Of([TimeSpan.FromSeconds(46442.119667)], "medium.en");

            Assert.True(AudioAuditFileIdentity.Matches(before, after));
            Assert.True(AudioAuditFileIdentity.SameFiles(before, after));
        }

        /// <summary>
        /// A rescan with a different tool can shift a duration by milliseconds, and that must
        /// not be enough to throw a transcript away - which is the failing this replaces.
        /// </summary>
        [Fact]
        [Trait("Method", "Of")]
        [Trait("Scenario", "MillisecondsOfJitterAreNotAChange")]
        public void Of_IsNotMovedByMeasurementJitter()
        {
            Assert.Equal(
                AudioAuditFileIdentity.Of([TimeSpan.FromSeconds(46442.119667)], "medium.en"),
                AudioAuditFileIdentity.Of([TimeSpan.FromSeconds(46442.004)], "medium.en"));
        }

        [Fact]
        [Trait("Method", "Matches")]
        [Trait("Scenario", "ADifferentRecording")]
        public void DoesNotMatch_ADifferentRecording()
        {
            // Two recordings of a book practically never run to the same second. An abridged
            // edition swapped in for an unabridged one is hours apart.
            Assert.False(AudioAuditFileIdentity.Matches(
                AudioAuditFileIdentity.Of([Mins(774)], "medium.en"),
                AudioAuditFileIdentity.Of([Mins(412)], "medium.en")));
        }

        /// <summary>The last file counts too: a book can be re-cut at its end alone.</summary>
        [Fact]
        [Trait("Method", "Matches")]
        [Trait("Scenario", "OnlyTheLastFileChanged")]
        public void DoesNotMatch_WhenOnlyTheLastFileChanged()
        {
            Assert.False(AudioAuditFileIdentity.Matches(
                AudioAuditFileIdentity.Of([Mins(774), Mins(136)], "medium.en"),
                AudioAuditFileIdentity.Of([Mins(774), Mins(97)], "medium.en")));
        }

        [Theory]
        [Trait("Method", "Matches")]
        [Trait("Scenario", "UnknownEitherSide")]
        [InlineData(null, "46442s@medium.en~3")]
        [InlineData("46442s@medium.en~3", null)]
        [InlineData("", "46442s@medium.en~3")]
        public void DoesNotMatch_WhenEitherSideIsUnknown(string? stored, string? current)
        {
            // A file with no measured length is not vouched for.
            Assert.False(AudioAuditFileIdentity.Matches(stored, current));
        }

        [Fact]
        [Trait("Method", "Of")]
        [Trait("Scenario", "ABetterModelIsADifferentListening")]
        public void Of_TreatsATranscriptTakenByAnotherModelAsStale()
        {
            // Moving from base to medium changed nothing for any book already audited until
            // the model went into the identity: the files had not moved, so every re-run
            // re-judged the old words and the better model was never asked.
            Assert.False(AudioAuditFileIdentity.Matches(
                AudioAuditFileIdentity.Of([Mins(774)], "base.en"),
                AudioAuditFileIdentity.Of([Mins(774)], "medium.en")));
        }

        [Fact]
        [Trait("Method", "SameFiles")]
        [Trait("Scenario", "HowWeListenedIsNotWhatWasAccepted")]
        public void SameFiles_IgnoresTheModelAndTheOffset()
        {
            // Someone vouched for a recording, not for the way it was transcribed. Changing
            // the model or the offset once lapsed twelve acceptances.
            var accepted = AudioAuditFileIdentity.Of([Mins(774)], "base.en");
            var now = AudioAuditFileIdentity.Of([Mins(774)], "medium.en", 2.6);

            Assert.True(AudioAuditFileIdentity.SameFiles(accepted, now));
            Assert.False(AudioAuditFileIdentity.Matches(accepted, now));
        }

        [Fact]
        [Trait("Method", "SameFiles")]
        [Trait("Scenario", "ASwappedRecordingStillBreaksIt")]
        public void SameFiles_StillNoticesADifferentRecording()
        {
            Assert.False(AudioAuditFileIdentity.SameFiles(
                AudioAuditFileIdentity.Of([Mins(774)], "medium.en"),
                AudioAuditFileIdentity.Of([Mins(412)], "medium.en")));
            Assert.False(AudioAuditFileIdentity.SameFiles(null, "46442s@medium.en~3"));
            Assert.False(AudioAuditFileIdentity.SameFiles("46442s@medium.en~3", null));
        }

        [Fact]
        [Trait("Method", "OpeningSkipOf")]
        [Trait("Scenario", "ARecordedSkipReadsBackExactly")]
        public void OpeningSkipOf_ReadsBackWhatOfWrote()
        {
            // It has to round-trip to the same string, or the identity it rebuilds will not
            // equal the one on the record and the transcript is thrown away.
            var written = AudioAuditFileIdentity.Of([Mins(774)], "medium.en", 10.7);

            var skip = AudioAuditFileIdentity.OpeningSkipOf(written);

            Assert.Equal(10.7, skip.TotalSeconds, 3);
            Assert.Equal(written, AudioAuditFileIdentity.Of([Mins(774)], "medium.en", skip.TotalSeconds));
        }

        [Fact]
        [Trait("Method", "OpeningSkipOf")]
        [Trait("Scenario", "NoSkipRecordedIsZero")]
        public void OpeningSkipOf_IsZeroWithoutOne()
        {
            Assert.Equal(TimeSpan.Zero, AudioAuditFileIdentity.OpeningSkipOf(null));
            Assert.Equal(TimeSpan.Zero, AudioAuditFileIdentity.OpeningSkipOf("46442s"));
            Assert.Equal(TimeSpan.Zero, AudioAuditFileIdentity.OpeningSkipOf(AudioAuditFileIdentity.Of([Mins(774)], "medium.en")));
        }

        /// <summary>
        /// The bridge for everything written before durations were recorded. Those identities
        /// hold the lengths of containers that have since been rewritten, so they cannot be
        /// compared with a duration and cannot be recovered into one. They are taken at their
        /// word instead, which keeps every acceptance already given and every transcript
        /// already taken.
        /// </summary>
        [Fact]
        [Trait("Method", "SameFiles")]
        [Trait("Scenario", "AnAcceptanceFromBeforeDurationsStillHolds")]
        public void SameFiles_HonoursAnIdentityWrittenTheOldWay()
        {
            // Verbatim from the library.
            const string acceptedLongAgo = "35460185:639252507960433027";

            Assert.True(AudioAuditFileIdentity.SameFiles(
                acceptedLongAgo,
                AudioAuditFileIdentity.Of([Mins(774)], "medium.en", 2.6)));
        }

        [Fact]
        [Trait("Method", "Matches")]
        [Trait("Scenario", "ATranscriptFromBeforeDurationsIsKept")]
        public void Matches_KeepsATranscriptWrittenTheOldWayWhenTheListeningAgrees()
        {
            // The words are good and re-taking them costs minutes of CPU a book across the
            // whole library. Only the files part is bridged.
            var current = AudioAuditFileIdentity.Of([Mins(774)], "medium.en", 2.7);
            var storedOldWay = "377814753:639257318581723427" + current[current.IndexOf('@')..];

            Assert.True(AudioAuditFileIdentity.Matches(storedOldWay, current));
        }

        [Fact]
        [Trait("Method", "Matches")]
        [Trait("Scenario", "TheBridgeDoesNotExcuseAWorseListening")]
        public void Matches_StillReHearsWhenTheListeningChanged()
        {
            // The bridge is about which files, not about how they were heard. A better model
            // or a better walk still re-hears everything, old identity or not.
            var current = AudioAuditFileIdentity.Of([Mins(774)], "medium.en", 2.7);

            Assert.False(AudioAuditFileIdentity.Matches(
                "377814753:639257318581723427@base.en~1+2.7",
                current));
        }
    }
}
