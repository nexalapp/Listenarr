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

namespace Listenarr.Tests.Features.Domain.Downloads
{
    /// <summary>
    /// Which size a block records, and why it is not the client's.
    ///
    /// A Usenet posting's advertised size includes its par2 set; what the client ends up
    /// with does not. The gap is about a tenth of the posting — the same width as the
    /// tolerance the match allows — so comparing one against the other decides nothing
    /// and sometimes decides wrongly. Measured on this library: 'Under Fire' was blocked
    /// at the client's 1,127,219,200 and offered back at the indexer's 1,250,951,168,
    /// which is 9.9% away against a 10% tolerance. It escaped its own block and was
    /// grabbed nine times in one day.
    /// </summary>
    [Trait("Name", "BlockedReleaseSizeProvenanceTests")]
    [Trait("Category", "Downloads")]
    public sealed class BlockedReleaseSizeProvenanceTests : BaseTests
    {
        private const string Title = "WEB Griffin - The Corps, Bk 9 - Under Fire (NMR";
        private const long Advertised = 1_250_951_168;
        private const long WhatTheClientGot = 1_127_219_200;

        private static Download Failed(long? expected, long total) => new()
        {
            Id = Guid.NewGuid().ToString(),
            Title = Title,
            ExpectedFileSize = expected,
            TotalSize = total
        };

        [Fact]
        public void For_RecordsTheAdvertisedSizeRatherThanWhatTheClientGot()
        {
            var entry = BlockedRelease.For(
                Failed(Advertised, WhatTheClientGot),
                DownloadFailureReason.Damaged,
                "par repair failed");

            Assert.Equal(Advertised, entry.SizeBytes);
        }

        [Fact]
        public void For_RecordsNoSizeWhenOnlyTheClientsFigureIsKnown()
        {
            // Null is not a gap in the record: it is the honest answer, and the match
            // reads it as "the fingerprint decides". A number that cannot be compared
            // with a search result must never be allowed to veto one.
            var entry = BlockedRelease.For(
                Failed(expected: null, total: WhatTheClientGot),
                DownloadFailureReason.Damaged,
                "par repair failed");

            Assert.Null(entry.SizeBytes);
        }

        [Fact]
        public void Matches_TheReleaseItWasBlockedFor()
        {
            var entry = BlockedRelease.For(
                Failed(Advertised, WhatTheClientGot),
                DownloadFailureReason.Damaged,
                "par repair failed");

            Assert.True(BlockedReleaseMatch.Matches(entry, Title, candidateReleaseId: null, Advertised));
        }

        [Fact]
        public void Matches_WhenTheBlockPredatesTheAdvertisedSizeBeingKept()
        {
            // The 41 rows already in this library's table, every one of them holding a
            // client figure. Repaired to null rather than reinterpreted, because the two
            // kinds of number are not distinguishable after the fact.
            var entry = BlockedRelease.For(
                Failed(expected: null, total: WhatTheClientGot),
                DownloadFailureReason.Damaged,
                "par repair failed");

            Assert.True(BlockedReleaseMatch.Matches(entry, Title, candidateReleaseId: null, Advertised));
        }

        [Fact]
        public void Matches_StillRefusesADifferentPostingOfTheSameTitle()
        {
            // The size is corroboration, not decoration: two sizes of the same kind that
            // are far apart are two different postings, and blocking one must not block
            // the other.
            var entry = BlockedRelease.For(
                Failed(Advertised, WhatTheClientGot),
                DownloadFailureReason.Damaged,
                "par repair failed");

            Assert.False(BlockedReleaseMatch.Matches(entry, Title, candidateReleaseId: null, Advertised * 2));
        }

        [Fact]
        public void TheGapThatLetUnderFireThrough_IsInsideTheTolerance_OnceBothSidesAreAdvertisedSizes()
        {
            // The regression itself, stated as arithmetic: compared against the client's
            // figure the difference is 123,731,968 over a 112,721,920 threshold and the
            // release walks. Compared like with like there is no difference at all.
            var escaped = Math.Abs(Advertised - WhatTheClientGot) > WhatTheClientGot * 0.1;
            Assert.True(escaped);

            var entry = BlockedRelease.For(
                Failed(Advertised, WhatTheClientGot),
                DownloadFailureReason.Damaged,
                "par repair failed");
            Assert.Equal(Advertised, entry.SizeBytes);
            Assert.True(BlockedReleaseMatch.Matches(entry, Title, candidateReleaseId: null, Advertised));
        }
    }
}
