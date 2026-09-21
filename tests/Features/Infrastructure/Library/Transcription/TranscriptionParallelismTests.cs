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
using Listenarr.Infrastructure.Library.Transcription;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Transcription
{
    [Trait("Name", "TranscriptionParallelismTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class TranscriptionParallelismTests : BaseTests
    {
        [Theory]
        [InlineData(2, 1, 1)]
        [InlineData(4, 1, 3)]
        [InlineData(8, 1, 7)]
        [InlineData(16, 2, 7)]
        [InlineData(28, 3, 9)]
        [InlineData(64, 4, 15)]
        public void SlotsAndThreads_SplitTheCoresIntoRunsOfAboutEight(int cores, int slots, int threads)
        {
            Assert.Equal(slots, TranscriptionParallelism.SlotsFor(cores));
            Assert.Equal(threads, TranscriptionParallelism.ThreadsFor(cores, slots));
        }

        [Fact]
        public void ThisMachine_GetsAtLeastOneSlotWithAtLeastOneThread()
        {
            Assert.InRange(TranscriptionParallelism.Slots, 1, TranscriptionParallelism.MaximumSlots);
            Assert.True(TranscriptionParallelism.ThreadsPerSlot >= 1);
        }
    }
}
