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
    [Trait("Name", "AudioIdentityMatcherTests")]
    [Trait("Category", "Domain")]
    public sealed class AudioIdentityMatcherTests : BaseTests
    {
        private const string Opening =
            "Macmillan Audio presents A War of Gifts, an Ender story, by Orson Scott Card. Read by Scott Brick. " +
            "1. Saint Nick. Zach Morgan sat attentively on the front row of the little sanctuary.";

        [Fact]
        public void Judge_MatchesWhenTheTitleAndAuthorAreHeard()
        {
            var result = AudioIdentityMatcher.Judge(Opening, "A War of Gifts", ["Orson Scott Card"], ["Scott Brick", "Stefan Rudnicki"], null);

            Assert.Equal(AudioAuditVerdict.Match, result.Verdict);
            Assert.Equal(1, result.TitleScore);
            Assert.Equal(1, result.AuthorScore);
            Assert.Equal(1, result.NarratorScore);
            Assert.Equal("Scott Brick", result.Credits.Narrator);
        }

        [Fact]
        public void Judge_ForgivesAMisheardName()
        {
            // Whisper wrote "Zach" for "Zeck" and "Meaker" for "Meeker".
            var result = AudioIdentityMatcher.Judge(
                "A War of Gifts by Orson Scot Card. Read by Scot Brick. Zach Morgan sat attentively on the front row of the sanctuary.",
                "A War of Gifts", ["Orson Scott Card"], ["Scott Brick"], null);

            Assert.Equal(AudioAuditVerdict.Match, result.Verdict);
            Assert.Equal(1, result.AuthorScore);
        }

        [Fact]
        public void Judge_UsesAnAliasForTheAuthor()
        {
            var aliases = new List<AuthorAlias> { new("Cory Doctorow", "Cory E. Doctorow") };
            var result = AudioIdentityMatcher.Judge(
                "Radicalized by Cory Doctorow, read by the author. A superhero finds himself way over his head in this story.",
                "Radicalized", ["Cory E. Doctorow"], null, aliases);

            Assert.Equal(AudioAuditVerdict.Match, result.Verdict);
            Assert.Equal(1, result.AuthorScore);
            Assert.Null(result.NarratorScore);
        }

        [Fact]
        public void Judge_FlagsADifferentNarrator()
        {
            var result = AudioIdentityMatcher.Judge(
                "A War of Gifts by Orson Scott Card, read by Stefan Rudnicki. Zach Morgan sat attentively on the front row of the sanctuary.",
                "A War of Gifts", ["Orson Scott Card"], ["Scott Brick"], null);

            Assert.Equal(AudioAuditVerdict.NarratorMismatch, result.Verdict);
            Assert.Contains("Stefan Rudnicki", result.Reason);
        }

        [Fact]
        public void Judge_CallsADifferentBookAMismatch()
        {
            var result = AudioIdentityMatcher.Judge(
                "The Forever War, by Joe Haldeman. Narrated by George Wilson. Private Mandella, you are hereby ordered to report.",
                "A War of Gifts", ["Orson Scott Card"], ["Scott Brick"], null);

            Assert.Equal(AudioAuditVerdict.Mismatch, result.Verdict);
            Assert.Contains("The Forever War", result.Reason);
            Assert.Equal("Joe Haldeman", result.Credits.Author);
        }

        [Fact]
        public void Judge_IsInconclusiveOnMusicOrSilence()
        {
            var result = AudioIdentityMatcher.Judge("[Music]", "A War of Gifts", ["Orson Scott Card"], null, null);
            Assert.Equal(AudioAuditVerdict.Inconclusive, result.Verdict);
        }

        [Fact]
        public void Judge_IsInconclusiveWhenOnlyFragmentsMatch()
        {
            // Prose that mentions "war" and "gifts" is not the credits.
            var result = AudioIdentityMatcher.Judge(
                "The war had gone on for years and the gifts were few and nobody remembered why it had started at all.",
                "A War of Gifts", ["Orson Scott Card"], null, null);

            Assert.NotEqual(AudioAuditVerdict.Match, result.Verdict);
            Assert.NotEqual(AudioAuditVerdict.Mismatch, result.Verdict);
        }

        [Fact]
        public void Judge_DoesNotMistakeAChapterNamedNarratorForAMismatch()
        {
            // No "read by" heard at all: the narrator is simply not credited aloud.
            var result = AudioIdentityMatcher.Judge(
                "A War of Gifts by Orson Scott Card. Zach Morgan sat attentively on the front row of the little sanctuary.",
                "A War of Gifts", ["Orson Scott Card"], ["Scott Brick"], null);

            Assert.Equal(AudioAuditVerdict.Match, result.Verdict);
        }
        [Fact]
        public void Judge_FlagsABookMissingMostOfItself()
        {
            // Ender's Shadow on disk: 6h 26m of a book that should run 15h 42m.
            var result = AudioIdentityMatcher.Judge(
                Opening, "A War of Gifts", ["Orson Scott Card"], ["Scott Brick"], null,
                recordedMinutes: 942, measuredMinutes: 386);

            Assert.Equal(AudioAuditVerdict.Incomplete, result.Verdict);
            Assert.Contains("59%", result.Reason);
            Assert.Contains("15h 42m", result.Reason);
            Assert.Contains("6h 26m", result.Reason);
        }

        [Fact]
        public void Judge_SaysNothingAboutAnAbridgementOrASlowerReading()
        {
            // A fifth short is the band where an abridgement and a different reading
            // cannot be told apart, and neither is a broken file.
            var result = AudioIdentityMatcher.Judge(
                Opening, "A War of Gifts", ["Orson Scott Card"], ["Scott Brick"], null,
                recordedMinutes: 1182, measuredMinutes: 1000);

            Assert.Equal(AudioAuditVerdict.Match, result.Verdict);
        }

        [Fact]
        public void Judge_NeverComplainsThatThereIsMoreBookThanExpected()
        {
            // Nothing is missing; the record simply describes a shorter edition.
            var result = AudioIdentityMatcher.Judge(
                Opening, "A War of Gifts", ["Orson Scott Card"], ["Scott Brick"], null,
                recordedMinutes: 121, measuredMinutes: 373);

            Assert.Equal(AudioAuditVerdict.Match, result.Verdict);
        }

        [Fact]
        public void Judge_NamesTheNarratorAheadOfTheMissingAudio()
        {
            // Both are true of a truncated wrong edition; the one that names a person first.
            var result = AudioIdentityMatcher.Judge(
                Opening, "A War of Gifts", ["Orson Scott Card"], ["Stefan Rudnicki"], null,
                recordedMinutes: 942, measuredMinutes: 386);

            Assert.Equal(AudioAuditVerdict.NarratorMismatch, result.Verdict);
        }

        [Fact]
        public void Judge_SaysNothingAboutLengthWhenTheRecordClaimsNone()
        {
            var result = AudioIdentityMatcher.Judge(
                Opening, "A War of Gifts", ["Orson Scott Card"], ["Scott Brick"], null,
                recordedMinutes: null, measuredMinutes: 370);

            Assert.Equal(AudioAuditVerdict.Match, result.Verdict);
        }

        [Fact]
        public void Judge_MissingAudioAloneFlagsAnOpeningWithNoCredits()
        {
            // Music under the opening, so nothing was heard - but two thirds of the book
            // is absent, and that is still worth saying.
            var result = AudioIdentityMatcher.Judge(
                "The war had gone on for years and the gifts were few and nobody remembered why it had started at all.",
                "A War of Gifts", ["Orson Scott Card"], null, null,
                recordedMinutes: 2096, measuredMinutes: 720);

            Assert.Equal(AudioAuditVerdict.Incomplete, result.Verdict);
            Assert.Contains("66%", result.Reason);
        }
        [Fact]
        public void Judge_HearsACompoundTheTranscriberSplitInTwo()
        {
            // Ironclads, read aloud correctly, written down as two words. The whole title
            // used to fail on that space.
            var result = AudioIdentityMatcher.Judge(
                "Iron Clads by Adrian Chikovsky, read by Peter Noble. Chapter 1. Sturgeon says that, way back when, the sons of the rich used to go to war.",
                "Ironclads", ["Adrian Tchaikovsky"], ["Peter Noble"], null);

            Assert.Equal(AudioAuditVerdict.Match, result.Verdict);
            Assert.Equal(1, result.TitleScore);
        }

        [Fact]
        public void Judge_ForgivesTwoEditsInALongName()
        {
            // "Tchaikovsky" heard as "Chikovsky": two edits in eleven letters is a syllable,
            // not a different author.
            Assert.True(AudioIdentityMatcher.Close("tchaikovsky", "chikovsky"));
            Assert.Equal(2, AudioIdentityMatcher.Slack("tchaikovsky".Length));
        }

        [Fact]
        public void Judge_StillRefusesTwoEditsInAShortWord()
        {
            // Two edits in five letters is most of the word, and "medusa" is not "melissa".
            Assert.False(AudioIdentityMatcher.Close("mars", "moon"));
            Assert.Equal(1, AudioIdentityMatcher.Slack("mars".Length));
        }
    }
}
