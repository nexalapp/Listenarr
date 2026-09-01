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
using System.Collections.Concurrent;

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// What a file's tags were last time it was probed, kept so the tag table does not
    /// spawn a probe per file on every load.
    ///
    /// <para>
    /// An entry is only used when the file's size and modification time still match what
    /// they were when it was read. That is what makes the cache correct without an
    /// invalidation call anywhere: a tag write replaces the file, so the next load misses
    /// and re-reads it. Nothing has to remember to tell the cache.
    /// </para>
    /// <para>
    /// Singleton and deliberately dependency-free — it is a dictionary, not a service, so
    /// the scoped things that use it can come and go with a request.
    /// </para>
    /// </summary>
    public sealed class LibraryTagCache
    {
        /// <summary>
        /// Enough for a large library several times over. The bound exists so a runaway
        /// scan cannot grow the process without limit, not because a real library
        /// approaches it.
        /// </summary>
        private const int MaxEntries = 50_000;

        private sealed record Entry(long Length, DateTime LastWriteUtc, AudiobookFileTags Tags);

        private readonly ConcurrentDictionary<string, Entry> _entries =
            new(StringComparer.Ordinal);

        /// <summary>The cached tags for a file at exactly this size and modification time.</summary>
        public AudiobookFileTags? TryGet(string path, long length, DateTime lastWriteUtc) =>
            _entries.TryGetValue(path, out var entry)
                && entry.Length == length
                && entry.LastWriteUtc == lastWriteUtc
                    ? entry.Tags
                    : null;

        public void Set(string path, long length, DateTime lastWriteUtc, AudiobookFileTags tags)
        {
            if (_entries.Count >= MaxEntries && !_entries.ContainsKey(path))
            {
                // Nothing here is worth an eviction policy: the table is rebuilt from
                // disk whenever it is asked for, so dropping the cache costs one slow
                // load rather than any correctness.
                _entries.Clear();
            }

            _entries[path] = new Entry(length, lastWriteUtc, tags);
        }

        public void Clear() => _entries.Clear();
    }
}
