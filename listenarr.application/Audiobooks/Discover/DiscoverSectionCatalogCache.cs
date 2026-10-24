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
namespace Listenarr.Application.Audiobooks.Discover
{
    /// <summary>
    /// Holds each source's index of sections for a few minutes.
    ///
    /// <para>
    /// The picker asks for it every time it opens, and the answer is a page fetched from
    /// Audible or the forum. Without this, opening and closing the panel three times is
    /// three requests for a list that changes a few times a year. A singleton on purpose:
    /// the point is to be shared.
    /// </para>
    /// </summary>
    public sealed class DiscoverSectionCatalogCache
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

        private readonly SemaphoreSlim _gate = new(1, 1);

        private readonly Dictionary<string, (DateTime FetchedAt, IReadOnlyList<DiscoverSectionOption> Options)> _entries =
            new(StringComparer.Ordinal);

        private readonly TimeProvider _time;

        public DiscoverSectionCatalogCache(TimeProvider? timeProvider = null) =>
            _time = timeProvider ?? TimeProvider.System;

        /// <summary>
        /// The cached index, or what <paramref name="load"/> returns. An empty answer is
        /// not cached: a source that was briefly unreachable must be asked again.
        /// </summary>
        public async Task<IReadOnlyList<DiscoverSectionOption>> GetAsync(
            string source,
            Func<CancellationToken, Task<IReadOnlyList<DiscoverSectionOption>>> load,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(load);

            await _gate.WaitAsync(cancellationToken);
            try
            {
                var now = _time.GetUtcNow().UtcDateTime;
                if (_entries.TryGetValue(source, out var cached) && now - cached.FetchedAt < Lifetime)
                {
                    return cached.Options;
                }

                var options = await load(cancellationToken);
                if (options.Count > 0)
                {
                    _entries[source] = (now, options);
                }

                return options;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
