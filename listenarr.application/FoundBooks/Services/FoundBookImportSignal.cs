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
using Listenarr.Application.FoundBooks.Contracts;

namespace Listenarr.Application.FoundBooks.Services
{
    /// <summary>
    /// One flag, set by whoever queues and cleared by the worker when it looks. A wake
    /// that arrives while the worker is busy is not lost: the flag stays set and the
    /// next wait returns at once.
    /// </summary>
    public sealed class FoundBookImportSignal : IFoundBookImportSignal
    {
        private readonly SemaphoreSlim _wake = new(0, 1);

        public void Wake()
        {
            try
            {
                _wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signalled; one wake is as good as many.
            }
        }

        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            _wake.WaitAsync(timeout, cancellationToken);
    }
}
