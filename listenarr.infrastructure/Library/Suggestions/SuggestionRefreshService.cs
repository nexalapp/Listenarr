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
using System.Threading.Channels;
using Listenarr.Application.Audiobooks.Suggestions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Suggestions
{
    /// <summary>
    /// Fetches author and series catalogs into the cache, one at a time, in the
    /// background.
    ///
    /// <para>
    /// Everything goes through the same catalog services the author and series pages
    /// use, so a refresh here leaves exactly what opening every one of those pages would
    /// have. The work is serialised with a pause between fetches because a whole library
    /// of authors is hundreds of Audible requests, and the point is a cache that fills
    /// up quietly, not a burst that gets the instance throttled.
    /// </para>
    /// </summary>
    public sealed class SuggestionRefreshService(
        IServiceScopeFactory scopeFactory,
        ILogger<SuggestionRefreshService> logger,
        TimeProvider? timeProvider = null) : ISuggestionRefreshService
    {
        private static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromDays(30);
        private static readonly TimeSpan PauseBetweenFetches = TimeSpan.FromSeconds(2);

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
        private readonly Channel<WorkItem> _queue = Channel.CreateUnbounded<WorkItem>();
        private readonly object _gate = new();
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private Task? _consumer;
        private int _completed;
        private int _total;
        private int _failed;
        private string? _current;
        private DateTime? _startedAt;
        private DateTime? _finishedAt;

        private sealed record WorkItem(bool IsSeries, string Name, bool Force);

        public SuggestionRefreshStatus Status
        {
            get
            {
                lock (_gate)
                {
                    return new SuggestionRefreshStatus(
                        _pending.Count > 0, _completed, _total, _current, _startedAt, _finishedAt, _failed);
                }
            }
        }

        public async Task<bool> RequestRefreshAsync(
            TimeSpan? staleAfter = null,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_pending.Count > 0)
                {
                    return false;
                }

                _completed = 0;
                _total = 0;
                _failed = 0;
                _finishedAt = null;
                _startedAt = _time.GetUtcNow().UtcDateTime;
            }

            var cutoff = _time.GetUtcNow().UtcDateTime - (staleAfter ?? DefaultStaleAfter);
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();

            var library = await repository.GetAllAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var memberships = await repository.GetAllSeriesMembershipsGroupedByAudiobookIdAsync(cancellationToken);
            var cachedAuthors = (await repository.GetAllCachedAuthorsAsync(cancellationToken))
                .ToDictionary(entry => SuggestionNames.Normalize(entry.AuthorName), entry => entry, StringComparer.Ordinal);
            var cachedSeries = (await repository.GetAllCachedSeriesAsync(cancellationToken))
                .ToDictionary(entry => SuggestionNames.Normalize(entry.SeriesName), entry => entry, StringComparer.Ordinal);

            var authors = library
                .SelectMany(book => book.Authors ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .DistinctBy(SuggestionNames.Normalize)
                .ToList();
            var series = library
                .SelectMany(book => (memberships.TryGetValue(book.Id, out var rows) ? rows.Select(row => row.SeriesName) : [])
                    .Append(book.Series))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .DistinctBy(SuggestionNames.Normalize)
                .ToList();

            var queued = 0;
            foreach (var author in authors)
            {
                var fresh = cachedAuthors.TryGetValue(SuggestionNames.Normalize(author), out var entry)
                    && entry.CatalogBooks is { Count: > 0 }
                    && (entry.LastFetchedAt ?? entry.UpdatedAt) >= cutoff;
                if (!fresh && Enqueue(new WorkItem(false, author, Force: entry != null)))
                {
                    queued++;
                }
            }

            foreach (var name in series)
            {
                var fresh = cachedSeries.TryGetValue(SuggestionNames.Normalize(name), out var entry)
                    && entry.CatalogBooks is { Count: > 0 }
                    && (entry.LastFetchedAt ?? entry.UpdatedAt) >= cutoff;
                if (!fresh && Enqueue(new WorkItem(true, name, Force: entry != null)))
                {
                    queued++;
                }
            }

            logger.LogInformation(
                "Suggestion refresh queued {Queued} catalog fetch(es) for {Authors} author(s) and {Series} series",
                queued,
                authors.Count,
                series.Count);

            if (queued == 0)
            {
                lock (_gate)
                {
                    _finishedAt = _time.GetUtcNow().UtcDateTime;
                }
            }

            return true;
        }

        public void QueueIfMissing(IEnumerable<string> authors, IEnumerable<string> series)
        {
            // Not forced: the catalog service returns straight from the cache when it
            // already holds the catalog, so a name already covered costs one lookup.
            foreach (var author in authors.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                Enqueue(new WorkItem(false, author.Trim(), Force: false));
            }

            foreach (var name in series.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                Enqueue(new WorkItem(true, name.Trim(), Force: false));
            }
        }

        private bool Enqueue(WorkItem item)
        {
            lock (_gate)
            {
                if (!_pending.Add(Key(item)))
                {
                    return false;
                }

                _total++;
                _startedAt ??= _time.GetUtcNow().UtcDateTime;
                _finishedAt = null;
                // Written and the consumer started under the same lock the consumer
                // takes to read, so an item cannot land just after it decided to stop.
                _queue.Writer.TryWrite(item);
                _consumer ??= Task.Run(ConsumeAsync);
            }

            return true;
        }

        private static string Key(WorkItem item) =>
            (item.IsSeries ? "series:" : "author:") + SuggestionNames.Normalize(item.Name);

        private async Task ConsumeAsync()
        {
            while (true)
            {
                WorkItem? item;
                lock (_gate)
                {
                    if (!_queue.Reader.TryRead(out item))
                    {
                        _consumer = null;
                        return;
                    }

                    _current = item.Name;
                }

                try
                {
                    await FetchAsync(item);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    lock (_gate)
                    {
                        _failed++;
                    }

                    logger.LogWarning(ex, "Suggestion refresh could not fetch a catalog for {Name}", item.Name);
                }

                lock (_gate)
                {
                    _pending.Remove(Key(item));
                    _completed++;
                    _current = null;
                    if (_pending.Count == 0)
                    {
                        _finishedAt = _time.GetUtcNow().UtcDateTime;
                    }
                }

                await Task.Delay(PauseBetweenFetches);
            }
        }

        private async Task FetchAsync(WorkItem item)
        {
            using var scope = scopeFactory.CreateScope();
            if (item.IsSeries)
            {
                var service = scope.ServiceProvider.GetRequiredService<ISeriesCatalogService>();
                await service.GetCatalogAsync(item.Name, forceRefresh: item.Force);
            }
            else
            {
                var service = scope.ServiceProvider.GetRequiredService<IAuthorCatalogService>();
                await service.GetCatalogAsync(item.Name, forceRefresh: item.Force);
            }
        }
    }
}
