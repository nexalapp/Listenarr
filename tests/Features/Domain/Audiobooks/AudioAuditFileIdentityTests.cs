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
        private static readonly DateTime When = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Matches_TheSameFilesUnchanged()
        {
            var taken = AudioAuditFileIdentity.Of([(89_927_466L, When), (12_345L, When)]);
            var now = AudioAuditFileIdentity.Of([(89_927_466L, When), (12_345L, When)]);

            Assert.True(AudioAuditFileIdentity.Matches(taken, now));
        }

        /// <summary>
        /// The case a timestamp alone misses: a different recording copied in with its
        /// modification time preserved, by cp -p, rsync -a, or a restore from backup.
        /// </summary>
        [Fact]
        public void DoesNotMatch_AReplacementThatKeptItsTimestamp()
        {
            var taken = AudioAuditFileIdentity.Of([(89_927_466L, When)]);
            var swapped = AudioAuditFileIdentity.Of([(322_004_118L, When)]);

            Assert.False(AudioAuditFileIdentity.Matches(taken, swapped));
        }

        [Fact]
        public void DoesNotMatch_AFileWrittenSince()
        {
            var taken = AudioAuditFileIdentity.Of([(89_927_466L, When)]);
            var rewritten = AudioAuditFileIdentity.Of([(89_927_466L, When.AddMinutes(1))]);

            Assert.False(AudioAuditFileIdentity.Matches(taken, rewritten));
        }

        /// <summary>The last file counts too: a book can be re-cut at its end alone.</summary>
        [Fact]
        public void DoesNotMatch_WhenOnlyTheLastFileChanged()
        {
            var taken = AudioAuditFileIdentity.Of([(100L, When), (200L, When)]);
            var now = AudioAuditFileIdentity.Of([(100L, When), (201L, When)]);

            Assert.False(AudioAuditFileIdentity.Matches(taken, now));
        }

        /// <summary>
        /// Unknown on either side is never a match: a transcript from before this check
        /// carries no identity and is re-taken once, and a file that cannot be measured
        /// is not vouched for.
        /// </summary>
        [Theory]
        [InlineData(null, "100:1")]
        [InlineData("100:1", null)]
        [InlineData("", "100:1")]
        [InlineData("100:1", "  ")]
        [InlineData(null, null)]
        public void DoesNotMatch_WhenEitherSideIsUnknown(string? stored, string? current)
        {
            Assert.False(AudioAuditFileIdentity.Matches(stored, current));
        }
        [Fact]
        [Trait("Method", "Of")]
        [Trait("Scenario", "ABetterModelIsADifferentListening")]
        public void Of_TreatsATranscriptTakenByAnotherModelAsStale()
        {
            var files = new[] { (1_000L, new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc)) };

            var heardByBase = AudioAuditFileIdentity.Of(files, "base.en");
            var heardByMedium = AudioAuditFileIdentity.Of(files, "medium.en");

            Assert.NotEqual(heardByBase, heardByMedium);
            Assert.False(AudioAuditFileIdentity.Matches(heardByBase, heardByMedium));
            Assert.True(AudioAuditFileIdentity.Matches(heardByMedium, heardByMedium));
        }

        [Fact]
        [Trait("Method", "Of")]
        [Trait("Scenario", "NoModelKeepsTheOldShape")]
        public void Of_WithoutAModelIsUnchanged()
        {
            var files = new[] { (1_000L, new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc)) };

            Assert.DoesNotContain("@", AudioAuditFileIdentity.Of(files));
        }
    }
}
