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
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.FoundBooks
{
    /// <summary>
    /// The wake between the queue and its worker: never lost, never counted.
    /// </summary>
    [Trait("Name", "FoundBookImportSignalTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookImportSignalTests : BaseTests
    {
        [Fact]
        public async Task AWakeBeforeTheWait_IsNotLost()
        {
            var signal = new FoundBookImportSignal();

            signal.Wake();

            Assert.True(await signal.WaitAsync(TimeSpan.Zero));
            Assert.False(await signal.WaitAsync(TimeSpan.Zero));
        }

        [Fact]
        public async Task ManyWakes_AreOneWake()
        {
            var signal = new FoundBookImportSignal();

            signal.Wake();
            signal.Wake();
            signal.Wake();

            Assert.True(await signal.WaitAsync(TimeSpan.Zero));
            Assert.False(await signal.WaitAsync(TimeSpan.Zero));
        }

        [Fact]
        public async Task AWaitWithNoWake_TimesOut()
        {
            var signal = new FoundBookImportSignal();

            Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(20)));
        }
    }
}
