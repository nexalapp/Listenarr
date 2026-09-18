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
    }
}
