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

namespace Listenarr.Application.Audiobooks.Transcription
{
    /// <summary>
    /// Remembers what a stretch of a file sounded like, keyed by the file's identity
    /// (path, length, last write), the stretch, and the model that listened.
    ///
    /// <para>
    /// A chapter-repair preview transcribes ten seconds after every mark of a ninety-mark
    /// book, and then the enqueue plans again from the same evidence. Without this the
    /// second pass would cost the same minute of CPU as the first and could, in
    /// principle, hear something different. Process-lifetime only: the transcript is
    /// cheap to redo after a restart and worthless once the file changes.
    /// </para>
    /// </summary>
    public sealed class TranscriptCache
    {
        private const int MaxEntries = 20_000;

        private readonly ConcurrentDictionary<string, Transcript> _entries = new(StringComparer.Ordinal);

        public Transcript? TryGet(string path, long length, DateTime lastWriteUtc, TimeSpan start, TimeSpan window, string? model = null) =>
            _entries.TryGetValue(Key(path, length, lastWriteUtc, start, window, model), out var transcript) ? transcript : null;

        public void Set(string path, long length, DateTime lastWriteUtc, TimeSpan start, TimeSpan window, Transcript transcript, string? model = null)
        {
            if (_entries.Count >= MaxEntries)
            {
                _entries.Clear();
            }

            _entries[Key(path, length, lastWriteUtc, start, window, model)] = transcript;
        }

        private static string Key(string path, long length, DateTime lastWriteUtc, TimeSpan start, TimeSpan window, string? model) =>
            $"{path}|{length}|{lastWriteUtc.Ticks}|{start.Ticks}|{window.Ticks}|{model?.Trim().ToLowerInvariant()}";
    }
}
