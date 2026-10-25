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
using Listenarr.Domain.Audiobooks.Discover;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// The discovery cache. Rows are small and are read a section at a time, so
    /// everything here is a straightforward query; the one piece of care is that an
    /// item keeps the moment it was first offered when it is seen again.
    /// </summary>
    public sealed class EfDiscoverRepository(ListenArrDbContext db) : IDiscoverRepository
    {
        public async Task<IReadOnlyList<DiscoverSection>> GetSectionsAsync(
            string source,
            CancellationToken cancellationToken = default) =>
            await db.DiscoverSections
                .AsNoTracking()
                .Where(section => section.Source == source)
                .OrderBy(section => section.Id)
                .ToListAsync(cancellationToken);

        public async Task SaveSectionAsync(
            string source,
            string sectionId,
            string sectionName,
            int pagesFetched,
            int? totalItems,
            bool exhausted,
            string? lastError,
            DateTime fetchedAt,
            CancellationToken cancellationToken = default)
        {
            var section = await db.DiscoverSections
                .FirstOrDefaultAsync(
                    row => row.Source == source && row.SectionId == sectionId, cancellationToken);

            if (section is null)
            {
                section = new DiscoverSection { Source = source, SectionId = sectionId };
                db.DiscoverSections.Add(section);
            }

            section.SectionName = sectionName;
            section.PagesFetched = pagesFetched;
            section.TotalItems = totalItems;
            section.Exhausted = exhausted;
            section.LastError = lastError;
            section.LastFetchedAt = fetchedAt;

            await db.SaveChangesAsync(cancellationToken);
            db.Entry(section).State = EntityState.Detached;
        }

        public async Task<IReadOnlyList<DiscoverItem>> GetItemsAsync(
            string source,
            IReadOnlyCollection<string> sectionIds,
            CancellationToken cancellationToken = default)
        {
            if (sectionIds.Count == 0)
            {
                return [];
            }

            var ids = sectionIds.ToList();
            return await db.DiscoverItems
                .AsNoTracking()
                .Where(item => item.Source == source && ids.Contains(item.SectionId))
                .OrderBy(item => item.Rank)
                .ToListAsync(cancellationToken);
        }

        public async Task<int> UpsertItemsAsync(
            string source,
            string sectionId,
            IReadOnlyList<DiscoverItem> items,
            int? replacePage = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(items);

            var existing = await db.DiscoverItems
                .Where(item => item.Source == source && item.SectionId == sectionId)
                .ToListAsync(cancellationToken);
            var byExternalId = existing.ToDictionary(item => item.ExternalId, StringComparer.Ordinal);

            var added = 0;
            foreach (var incoming in items)
            {
                if (byExternalId.TryGetValue(incoming.ExternalId, out var row))
                {
                    // Seen before: where it sits and what it says can change, when it
                    // first turned up cannot.
                    row.Rank = incoming.Rank;
                    row.Page = incoming.Page;
                    row.UpdatedAt = incoming.UpdatedAt;
                    row.PostedAt = incoming.PostedAt ?? row.PostedAt;
                    row.SourceUrl = incoming.SourceUrl ?? row.SourceUrl;
                    row.SourceNote = incoming.SourceNote ?? row.SourceNote;

                    // A row already looked up keeps the record that lookup found; the
                    // source's own description would overwrite it with less.
                    if (!row.Enriched)
                    {
                        row.Book = incoming.Book;
                        row.Enriched = incoming.Enriched;
                    }

                    continue;
                }

                // The section a row belongs to is this call's, not whatever the
                // caller happened to put on the object.
                incoming.Source = source;
                incoming.SectionId = sectionId;
                db.DiscoverItems.Add(incoming);
                added++;
            }

            if (replacePage is { } page)
            {
                var kept = items.Select(item => item.ExternalId).ToHashSet(StringComparer.Ordinal);
                var gone = existing
                    .Where(item => item.Page == page && !kept.Contains(item.ExternalId))
                    .ToList();
                db.DiscoverItems.RemoveRange(gone);
            }

            await db.SaveChangesAsync(cancellationToken);
            foreach (var entry in db.ChangeTracker.Entries<DiscoverItem>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            return added;
        }

        public async Task<int> TrimSectionAsync(
            string source,
            string sectionId,
            int keepNewest,
            CancellationToken cancellationToken = default)
        {
            if (keepNewest <= 0)
            {
                return 0;
            }

            var rows = await db.DiscoverItems
                .Where(item => item.Source == source && item.SectionId == sectionId)
                .ToListAsync(cancellationToken);
            if (rows.Count <= keepNewest)
            {
                return 0;
            }

            var surplus = rows
                .OrderByDescending(item => item.PostedAt ?? item.FirstSeenAt)
                .ThenByDescending(item => item.Id)
                .Skip(keepNewest)
                .ToList();

            db.DiscoverItems.RemoveRange(surplus);
            await db.SaveChangesAsync(cancellationToken);
            return surplus.Count;
        }

        public async Task<IReadOnlyList<DiscoverItem>> GetUnresolvedAsync(
            string source,
            int limit,
            CancellationToken cancellationToken = default) =>
            await db.DiscoverItems
                .AsNoTracking()
                .Where(item => item.Source == source && !item.Enriched)
                // Newest first: a book posted an hour ago is the one somebody is looking
                // at, and the backlog behind it can wait.
                .OrderByDescending(item => item.PostedAt ?? item.FirstSeenAt)
                .ThenByDescending(item => item.Id)
                .Take(Math.Max(1, limit))
                .ToListAsync(cancellationToken);

        public async Task<bool> ResolveItemAsync(
            int id,
            DiscoverBook book,
            DateTime resolvedAt,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(book);

            var item = await db.DiscoverItems.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
            if (item is null)
            {
                return false;
            }

            item.Book = book;
            item.Enriched = true;
            item.UpdatedAt = resolvedAt;

            await db.SaveChangesAsync(cancellationToken);
            db.Entry(item).State = EntityState.Detached;
            return true;
        }

        public async Task<int> PruneSectionsAsync(
            string source,
            IReadOnlyCollection<string> keepSectionIds,
            CancellationToken cancellationToken = default)
        {
            var keep = keepSectionIds.ToList();

            // Load-then-remove rather than ExecuteDelete so the in-memory provider the
            // tests run on behaves the same as SQLite.
            var items = await db.DiscoverItems
                .Where(item => item.Source == source && !keep.Contains(item.SectionId))
                .ToListAsync(cancellationToken);
            var sections = await db.DiscoverSections
                .Where(section => section.Source == source && !keep.Contains(section.SectionId))
                .ToListAsync(cancellationToken);

            if (items.Count == 0 && sections.Count == 0)
            {
                return 0;
            }

            db.DiscoverItems.RemoveRange(items);
            db.DiscoverSections.RemoveRange(sections);
            await db.SaveChangesAsync(cancellationToken);
            return items.Count + sections.Count;
        }
    }
}
