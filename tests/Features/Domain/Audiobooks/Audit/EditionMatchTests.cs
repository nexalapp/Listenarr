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

namespace Listenarr.Tests.Features.Domain.Audiobooks.Audit
{
    [Trait("Name", "EditionMatchTests")]
    [Trait("Category", "Domain")]
    public sealed class EditionMatchTests : BaseTests
    {
        // The two editions of Ender's Shadow, with the runtimes Audible and OverDrive
        // both give, and the file this library actually holds.
        private static readonly EditionCandidate Macmillan =
            new("B002UZL1CY", "Ender's Shadow", ["Scott Brick", "Gabrielle de Cuir"], "Macmillan Audio", 942);

        private static readonly EditionCandidate Phoenix =
            new("B0BG33CYXS", "Ender's Shadow", ["Michael Gross"], "Phoenix Books", 374);

        private const double FileMinutes = 386.4;

        [Fact]
        [Trait("Method", "Judge")]
        [Trait("Scenario", "FileNamesADifferentEdition")]
        public void Judge_NamesTheEditionTheFileActuallyIs()
        {
            var result = EditionMatch.Judge(FileMinutes, "B002UZL1CY", [Macmillan, Phoenix]);

            Assert.Equal(EditionMatchOutcome.OtherEdition, result.Outcome);
            Assert.Equal("B0BG33CYXS", result.Best!.Id);
            Assert.Contains("Michael Gross", result.Reason);
            Assert.Contains("Phoenix Books", result.Reason);
        }

        [Fact]
        [Trait("Method", "Judge")]
        [Trait("Scenario", "RecordAlreadyRight")]
        public void Judge_SaysNothingIsWrongWhenTheRecordAlreadyNamesThatEdition()
        {
            var result = EditionMatch.Judge(FileMinutes, "B0BG33CYXS", [Macmillan, Phoenix]);

            Assert.Equal(EditionMatchOutcome.Agrees, result.Outcome);
        }

        [Fact]
        [Trait("Method", "Judge")]
        [Trait("Scenario", "TwoEditionsOfSimilarLength")]
        public void Judge_RefusesToChooseBetweenTwoReadingsOfSimilarLength()
        {
            var otherUnabridged =
                new EditionCandidate("B000AAA111", "Ender's Shadow", ["Someone Else"], "Another Press", 380);

            var result = EditionMatch.Judge(FileMinutes, "B002UZL1CY", [Macmillan, Phoenix, otherUnabridged]);

            Assert.Equal(EditionMatchOutcome.Ambiguous, result.Outcome);
            Assert.NotNull(result.RunnerUp);
        }

        [Fact]
        [Trait("Method", "Judge")]
        [Trait("Scenario", "CassetteRipInNoCatalogue")]
        public void Judge_RefusesToSnapACassetteRipOntoTheNearestModernRelease()
        {
            // The population this exists for is old rips that no catalogue lists. The
            // nearest edition being 15h away is not a reason to call the file 15h.
            var result = EditionMatch.Judge(FileMinutes, "B002UZL1CY", [Macmillan]);

            Assert.Equal(EditionMatchOutcome.NoCandidate, result.Outcome);
            Assert.Null(result.Best);
        }

        [Fact]
        [Trait("Method", "Judge")]
        [Trait("Scenario", "NarratorCorroborates")]
        public void Judge_SaysWhenTheHeardNarratorAgreesWithTheLength()
        {
            var result = EditionMatch.Judge(FileMinutes, "B002UZL1CY", [Macmillan, Phoenix], ["Michael Gross"]);

            Assert.True(result.NarratorAgrees);
            Assert.Contains("names the same edition", result.Reason);
        }

        [Fact]
        [Trait("Method", "Judge")]
        [Trait("Scenario", "LengthAndVoiceDisagree")]
        public void Judge_OffersNothingWhenLengthAndVoiceDisagree()
        {
            // Mercury Rests on this library: the length lands on an AI narration while the
            // audio plainly credits someone else. One of the two is describing a different
            // recording and a proposal would write in the wrong one.
            var result = EditionMatch.Judge(FileMinutes, "B002UZL1CY", [Macmillan, Phoenix], ["Scott Brick"]);

            Assert.Equal(EditionMatchOutcome.Contested, result.Outcome);
            Assert.False(result.NarratorAgrees);
            Assert.Contains("Length and voice disagree", result.Reason);
        }

        [Fact]
        [Trait("Method", "Judge")]
        [Trait("Scenario", "NothingToCheckWith")]
        public void Judge_SaysNothingWithoutALengthOrACatalogue()
        {
            Assert.Equal(EditionMatchOutcome.Unknown, EditionMatch.Judge(null, "x", [Phoenix]).Outcome);
            Assert.Equal(EditionMatchOutcome.Unknown, EditionMatch.Judge(FileMinutes, "x", []).Outcome);
            Assert.Equal(
                EditionMatchOutcome.Unknown,
                EditionMatch.Judge(FileMinutes, "x", [Phoenix with { RuntimeMinutes = null }]).Outcome);
        }
        [Fact]
        [Trait("Method", "Judge")]
        [Trait("Scenario", "VoiceBreaksALengthTie")]
        public void Judge_LetsTheHeardVoiceBreakALengthTie()
        {
            // Airframe on this library: two editions eleven hours long and four minutes
            // apart. Length cannot separate them; the narrator can.
            var blom = new EditionCandidate("B111", "Airframe", ["Thomas Blom"], "Lindhardt og Ringhof", 665);
            var cassidy = new EditionCandidate("B222", "Airframe", ["Frances Cassidy"], "Random House Audio", 669);

            var result = EditionMatch.Judge(665, "B999", [blom, cassidy], ["Frances Cassidy"]);

            Assert.Equal(EditionMatchOutcome.OtherEdition, result.Outcome);
            Assert.Equal("B222", result.Best!.Id);
            Assert.True(result.NarratorAgrees);
        }

        [Fact]
        [Trait("Method", "Judge")]
        [Trait("Scenario", "VoiceMatchesNeitherTiedEdition")]
        public void Judge_SaysWhenTheHeardVoiceReadsNeitherTiedEdition()
        {
            // The real case: the audio names Garrick Hagon and no catalogued edition of
            // that length is his, so the edition on disk is not among the candidates.
            var blom = new EditionCandidate("B111", "Airframe", ["Thomas Blom"], "Lindhardt og Ringhof", 665);
            var cassidy = new EditionCandidate("B222", "Airframe", ["Frances Cassidy"], "Random House Audio", 669);

            var result = EditionMatch.Judge(665, "B222", [blom, cassidy], ["Garrick Hagon"]);

            Assert.Equal(EditionMatchOutcome.Ambiguous, result.Outcome);
            Assert.Contains("reads neither", result.Reason);
            Assert.False(result.NarratorAgrees);
        }

        [Fact]
        [Trait("Method", "Judge")]
        [Trait("Scenario", "NoVoiceLeavesATieATie")]
        public void Judge_LeavesATieATieWhenNothingWasHeard()
        {
            var blom = new EditionCandidate("B111", "Airframe", ["Thomas Blom"], "Lindhardt og Ringhof", 665);
            var cassidy = new EditionCandidate("B222", "Airframe", ["Frances Cassidy"], "Random House Audio", 669);

            var result = EditionMatch.Judge(665, "B999", [blom, cassidy]);

            Assert.Equal(EditionMatchOutcome.Ambiguous, result.Outcome);
            Assert.DoesNotContain("reads neither", result.Reason);
            Assert.Null(result.NarratorAgrees);
        }
    }
}
