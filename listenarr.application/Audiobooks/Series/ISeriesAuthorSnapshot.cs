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
namespace Listenarr.Application.Audiobooks.Series
{
    /// <summary>
    /// The author each series files under, as last computed from the library, for the
    /// naming renderer - which is synchronous and cannot query for it. See
    /// <see cref="SeriesAuthorRule"/> for the rule and the settings snapshot for the
    /// pattern. Refreshed after library saves and on a timer by the infrastructure.
    /// </summary>
    public interface ISeriesAuthorSnapshot
    {
        string? AuthorFor(string? series);

        void Update(IReadOnlyDictionary<string, string> authorsBySeriesKey);

        /// <summary>A save touched the library; the next refresh should not wait for the timer.</summary>
        void MarkStale();

        bool IsStale { get; }
    }

    public sealed class SeriesAuthorSnapshot : ISeriesAuthorSnapshot
    {
        private volatile IReadOnlyDictionary<string, string> _current =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private volatile bool _stale = true;

        public bool IsStale => _stale;

        public string? AuthorFor(string? series) =>
            string.IsNullOrWhiteSpace(series)
                ? null
                : _current.TryGetValue(SeriesAuthorRule.Key(series), out var author) ? author : null;

        public void Update(IReadOnlyDictionary<string, string> authorsBySeriesKey)
        {
            _current = authorsBySeriesKey;
            _stale = false;
        }

        public void MarkStale() => _stale = true;
    }
}
