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
    [Trait("Name", "AudioAuditAcceptanceTests")]
    [Trait("Category", "Domain")]
    public sealed class AudioAuditAcceptanceTests : BaseTests
    {
        private const string Files = "89927466:639251721393432443";
        private const string OtherFiles = "322004118:639251721393432443";

        private static Audiobook Flagged(
            AudioAuditVerdict verdict = AudioAuditVerdict.NarratorMismatch,
            string? identity = Files,
            string? accepted = null) => new()
            {
                Title = "Eaters of the Dead",
                AudioAuditVerdict = verdict,
                AudioAuditFileIdentity = identity,
                AudioAuditAcceptedIdentity = accepted
            };

        [Fact]
        public void NeedsAttention_WhenNobodyHasOverruledTheVerdict()
        {
            Assert.True(AudioAuditAcceptance.NeedsAttention(Flagged()));
            Assert.True(AudioAuditAcceptance.NeedsAttention(Flagged(AudioAuditVerdict.Mismatch)));
        }

        [Fact]
        public void NeedsAttention_IsFalseOnceSomeoneHasListenedAndAccepted()
        {
            var book = Flagged(accepted: Files);

            Assert.True(AudioAuditAcceptance.IsAccepted(book));
            Assert.False(AudioAuditAcceptance.NeedsAttention(book));
        }

        /// <summary>
        /// A person vouches for the recording they listened to, not for whatever occupies
        /// that path afterwards. A swapped file is judged on its own.
        /// </summary>
        [Fact]
        public void Acceptance_LapsesWhenTheFilesChange()
        {
            var book = Flagged(identity: OtherFiles, accepted: Files);

            Assert.False(AudioAuditAcceptance.IsAccepted(book));
            Assert.True(AudioAuditAcceptance.NeedsAttention(book));
        }

        /// <summary>
        /// Listening again to the same recording must not undo an acceptance, or every
        /// re-run would put the same books back in the queue.
        /// </summary>
        [Fact]
        public void Acceptance_SurvivesAnotherAuditOfTheSameFiles()
        {
            var book = Flagged(accepted: Files);
            book.AudioAuditFileIdentity = Files;

            Assert.True(AudioAuditAcceptance.IsAccepted(book));
        }

        [Theory]
        [InlineData(AudioAuditVerdict.Match)]
        [InlineData(AudioAuditVerdict.Inconclusive)]
        [InlineData(AudioAuditVerdict.NotAudited)]
        public void NeedsAttention_IsOnlyForAVerdictThatDisagrees(AudioAuditVerdict verdict)
        {
            Assert.False(AudioAuditAcceptance.NeedsAttention(Flagged(verdict)));
        }

        /// <summary>Nothing to pin an acceptance to is nothing accepted.</summary>
        [Fact]
        public void IsAccepted_IsFalseWithoutAFileIdentityOnEitherSide()
        {
            Assert.False(AudioAuditAcceptance.IsAccepted(Flagged(identity: null, accepted: Files)));
            Assert.False(AudioAuditAcceptance.IsAccepted(Flagged(accepted: null)));
        }
    }
}
