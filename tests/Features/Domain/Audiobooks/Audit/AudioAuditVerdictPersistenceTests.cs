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
    [Trait("Name", "AudioAuditVerdictPersistenceTests")]
    [Trait("Category", "Domain")]
    public sealed class AudioAuditVerdictPersistenceTests : BaseTests
    {
        /// <summary>
        /// Every verdict name that has ever been written to a database.
        ///
        /// <para>
        /// The column is mapped with <c>HasConversion&lt;string&gt;()</c>, so the member
        /// name <em>is</em> the stored value. Renaming one orphans every row holding the
        /// old name: EF cannot map it back and throws on any query that touches that
        /// audiobook, which took the library page down once already. This list is the
        /// reminder — adding a member is free, changing or removing one needs a data
        /// migration alongside it, and then this list.
        /// </para>
        /// </summary>
        private static readonly string[] Persisted =
        [
            "NotAudited",
            "Match",
            "NarratorMismatch",
            "Mismatch",
            "Inconclusive",
            "Incomplete"
        ];

        [Fact]
        [Trait("Method", "ToString")]
        [Trait("Scenario", "StoredNamesAreStable")]
        public void Verdicts_KeepTheNamesTheyAreStoredUnder()
        {
            var names = Enum.GetNames<AudioAuditVerdict>();

            Assert.Equal(Persisted.Order(), names.Order());
        }

        [Fact]
        [Trait("Method", "Of")]
        [Trait("Scenario", "EveryVerdictHasAnApiName")]
        public void EveryVerdictExceptNotAuditedHasAnApiName()
        {
            foreach (var verdict in Enum.GetValues<AudioAuditVerdict>())
            {
                var name = AudioAuditVerdictNames.Of(verdict);
                if (verdict == AudioAuditVerdict.NotAudited)
                {
                    Assert.Null(name);
                }
                else
                {
                    Assert.False(string.IsNullOrWhiteSpace(name), $"{verdict} has no API name.");
                }
            }
        }
    }
}
