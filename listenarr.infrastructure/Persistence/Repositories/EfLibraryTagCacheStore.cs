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
using System.Text.Json;
using Listenarr.Application.Audiobooks.Tagging;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// The tag cache's rows in SQLite. The probe result is stored as JSON: the table
    /// only ever reads a row back whole, keyed by path, so nothing inside it needs a
    /// column.
    /// </summary>
    public sealed class EfLibraryTagCacheStore(ListenArrDbContext db) : ILibraryTagCacheStore
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public async Task<IReadOnlyList<LibraryTagCacheRecord>> LoadAsync(CancellationToken cancellationToken = default)
        {
            var rows = await db.LibraryTagCacheEntries.AsNoTracking().ToListAsync(cancellationToken);
            var records = new List<LibraryTagCacheRecord>(rows.Count);
            foreach (var row in rows)
            {
                AudiobookFileTags? tags;
                try
                {
                    tags = JsonSerializer.Deserialize<AudiobookFileTags>(row.TagsJson, Json);
                }
                catch (JsonException)
                {
                    // A row this build cannot read is just a miss; the file gets probed.
                    continue;
                }

                if (tags != null)
                {
                    // The probe hands back a case-insensitive map and the planner looks
                    // tags up by catalog casing; JSON round-trips the entries, not the
                    // comparer.
                    tags = tags with
                    {
                        Tags = new Dictionary<string, string>(tags.Tags, StringComparer.OrdinalIgnoreCase)
                    };
                    records.Add(new LibraryTagCacheRecord(row.Path, row.Length, row.LastWriteUtc, tags));
                }
            }

            return records;
        }

        public async Task SaveAsync(IReadOnlyList<LibraryTagCacheRecord> records, CancellationToken cancellationToken = default)
        {
            if (records.Count == 0)
            {
                return;
            }

            var paths = records.Select(record => record.Path).ToList();
            var existing = await db.LibraryTagCacheEntries
                .Where(entry => paths.Contains(entry.Path))
                .ToDictionaryAsync(entry => entry.Path, StringComparer.Ordinal, cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var record in records)
            {
                if (!existing.TryGetValue(record.Path, out var entry))
                {
                    entry = new LibraryTagCacheEntry { Path = record.Path };
                    db.LibraryTagCacheEntries.Add(entry);
                }

                entry.Length = record.Length;
                entry.LastWriteUtc = record.LastWriteUtc;
                entry.TagsJson = JsonSerializer.Serialize(record.Tags, Json);
                entry.ReadAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        public Task ClearAsync(CancellationToken cancellationToken = default) =>
            db.LibraryTagCacheEntries.ExecuteDeleteAsync(cancellationToken);
    }
}
