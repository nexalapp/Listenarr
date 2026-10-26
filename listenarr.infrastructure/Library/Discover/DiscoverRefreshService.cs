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
using Listenarr.Application.Audiobooks.Discover;
using Listenarr.Domain.Audiobooks.Discover;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Discover
{
    /// <summary>
    /// Fills the discovery cache from the sources, a page at a time.
    ///
    /// <para>
    /// Three kinds of work, in the order they matter. The head of a followed section
    /// goes stale first — that is where anything new appears — so a head older than a
    /// few hours is re-read before anything else. Then the posts whose books nobody has
    /// looked up yet, because a row with no cover is half a row. Last, depth: the next
    /// page nobody has read, which is how a chart fills out to a hundred and a board
    /// back to a fortnight ago.
    /// </para>
    /// <para>
    /// One page per call, and one call per background tick. A refresh button does the
    /// heads of every followed section back to back, which is a handful of requests, not
    /// a crawl.
    /// </para>
    /// </summary>
    public sealed class DiscoverRefreshService(
        IServiceScopeFactory scopeFactory,
        ILogger<DiscoverRefreshService> logger,
        TimeProvider? timeProvider = null) : IDiscoverRefreshService
    {
        /// <summary>How long the first page of a section is trusted before it is re-read.</summary>
        private static readonly TimeSpan HeadStaleAfter = TimeSpan.FromHours(6);

        private static readonly TimeSpan PauseBetweenFetches = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Between metadata lookups, which go to Audible rather than to a person's
        /// forum and so need less room.
        /// </summary>
        private static readonly TimeSpan PauseBetweenLookups = TimeSpan.FromMilliseconds(750);

        /// <summary>
        /// How many forum posts a run looks up once it has read its pages. A board page
        /// is twenty titles, and at one lookup per background tick a page would take
        /// until tomorrow to grow its covers; a run someone asked for does the lot.
        /// </summary>
        private const int LookupsPerRun = 25;

        /// <summary>
        /// How many items a board keeps. Five pages is a hundred, and a board that has
        /// been followed for months would otherwise grow without limit.
        /// </summary>
        private const int BoardRetention = 500;

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
        private readonly Dictionary<string, DiscoverSourceProgress> _states = new(StringComparer.Ordinal)
        {
            [DiscoverSources.AudibleCharts] = new DiscoverSourceProgress(),
            [DiscoverSources.AbookLink] = new DiscoverSourceProgress()
        };

        public DiscoverRefreshStatus StatusFor(string source) =>
            _states.TryGetValue(source, out var state) ? state.Snapshot() : DiscoverRefreshStatus.Idle;

        public Task<bool> RequestRefreshAsync(string source, CancellationToken cancellationToken = default) =>
            StartAsync(source, head: true, cancellationToken);

        public Task<bool> RequestMoreAsync(string source, CancellationToken cancellationToken = default) =>
            StartAsync(source, head: false, cancellationToken);

        public async Task<string?> FetchNextNeededAsync(CancellationToken cancellationToken = default)
        {
            foreach (var source in _states.Keys)
            {
                if (_states[source].Running)
                {
                    continue;
                }

                var work = await NextWorkAsync(source, cancellationToken);
                if (work is null)
                {
                    continue;
                }

                if (!_states[source].TryBegin(1))
                {
                    continue;
                }

                try
                {
                    return await RunUnitAsync(source, work, cancellationToken);
                }
                finally
                {
                    _states[source].Finish();
                }
            }

            return null;
        }

        /// <summary>
        /// Starts a run over every followed section. The caller is answered as soon as
        /// the run is accepted; a page fetch takes seconds and the page polls the status.
        /// </summary>
        private async Task<bool> StartAsync(string source, bool head, CancellationToken cancellationToken)
        {
            if (!DiscoverSources.IsKnown(source) || !_states.TryGetValue(source, out var state))
            {
                return false;
            }

            var plan = await PlanAsync(source, head, cancellationToken);
            if (plan.Count == 0 || !state.TryBegin(plan.Count))
            {
                return false;
            }

            // Detached on purpose: the work outlives the request that asked for it, and
            // must not be cancelled when that request's token is disposed.
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var unit in plan)
                    {
                        await RunUnitAsync(source, unit, CancellationToken.None);
                        await Task.Delay(PauseBetweenFetches, CancellationToken.None);
                    }

                    await LookUpBacklogAsync(source, state, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    logger.LogWarning(ex, "Discovery refresh for {Source} stopped early", source);
                }
                finally
                {
                    state.Finish();
                }
            }, CancellationToken.None);

            return true;
        }

        /// <summary>
        /// Looks up the books the pages just found, so a tab someone is watching fills
        /// in its covers while they watch rather than over the next two hours. Bounded,
        /// because the backlog of a board followed for months is not what the person
        /// pressing Refresh is waiting for.
        /// </summary>
        private async Task LookUpBacklogAsync(
            string source,
            DiscoverSourceProgress state,
            CancellationToken cancellationToken)
        {
            List<DiscoverItem> pending;
            using (var scope = scopeFactory.CreateScope())
            {
                var context = Resolve(scope, source);
                pending = (await context.Discover.GetUnresolvedAsync(source, LookupsPerRun, cancellationToken))
                    .ToList();
            }

            if (pending.Count == 0)
            {
                return;
            }

            state.AddToTotal(pending.Count);
            foreach (var item in pending)
            {
                await RunUnitAsync(source, new WorkItem(null, 0, item), cancellationToken);
                await Task.Delay(PauseBetweenLookups, cancellationToken);
            }
        }

        /// <summary>
        /// What a run would do: the first page of every followed section, or the next
        /// page of each when asked for more.
        /// </summary>
        private async Task<List<WorkItem>> PlanAsync(string source, bool head, CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var context = Resolve(scope, source);
            var selection = await context.SelectionAsync(cancellationToken);
            var sections = await context.Discover.GetSectionsAsync(source, cancellationToken);
            var byId = sections.ToDictionary(section => section.SectionId, StringComparer.Ordinal);

            var plan = new List<WorkItem>();
            foreach (var section in selection.Sections)
            {
                byId.TryGetValue(section.Id, out var state);
                if (head)
                {
                    plan.Add(new WorkItem(section, 1));
                    continue;
                }

                var next = (state?.PagesFetched ?? 0) + 1;
                if (state?.Exhausted != true && next <= selection.PagesPerSection)
                {
                    plan.Add(new WorkItem(section, next));
                }
            }

            return plan;
        }

        /// <summary>
        /// The single most useful thing to do for a source right now, or null when there
        /// is nothing. Order: a stale head, then an unlooked-up post, then depth.
        /// </summary>
        private async Task<WorkItem?> NextWorkAsync(string source, CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var context = Resolve(scope, source);
            var selection = await context.SelectionAsync(cancellationToken);
            if (selection.Sections.Count == 0)
            {
                return null;
            }

            if (await context.Reader.UnavailableReasonAsync(cancellationToken) is not null)
            {
                return null;
            }

            var sections = (await context.Discover.GetSectionsAsync(source, cancellationToken))
                .ToDictionary(section => section.SectionId, StringComparer.Ordinal);
            var now = _time.GetUtcNow().UtcDateTime;

            foreach (var section in selection.Sections)
            {
                sections.TryGetValue(section.Id, out var state);
                if (state is null || state.LastFetchedAt is null || now - state.LastFetchedAt.Value > HeadStaleAfter)
                {
                    return new WorkItem(section, 1);
                }
            }

            var unresolved = await context.Discover.GetUnresolvedAsync(source, 1, cancellationToken);
            if (unresolved.Count > 0)
            {
                return new WorkItem(null, 0, unresolved[0]);
            }

            foreach (var section in selection.Sections)
            {
                sections.TryGetValue(section.Id, out var state);
                var next = (state?.PagesFetched ?? 0) + 1;
                if (state?.Exhausted != true && next <= selection.PagesPerSection)
                {
                    return new WorkItem(section, next);
                }
            }

            return null;
        }

        private async Task<string?> RunUnitAsync(string source, WorkItem unit, CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var context = Resolve(scope, source);
            var state = _states[source];

            try
            {
                if (unit.Item is not null)
                {
                    state.Current = unit.Item.Book.Title;
                    var resolved = await ResolveAsync(context, unit.Item, cancellationToken);
                    state.Completed++;
                    return resolved ? $"metadata for {unit.Item.Book.Title}" : null;
                }

                var section = unit.Section!;
                state.Current = $"{section.Name} page {unit.Page}";
                var fetched = await FetchPageAsync(context, source, section, unit.Page, cancellationToken);
                state.Completed++;
                if (!fetched)
                {
                    state.Failed++;
                    return null;
                }

                return $"{section.Name} page {unit.Page}";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Discovery fetch failed for {Source}", source);
                state.Completed++;
                state.Failed++;
                return null;
            }
            finally
            {
                state.Current = null;
            }
        }

        private async Task<bool> FetchPageAsync(
            SourceContext context,
            string source,
            DiscoverSelectedSection section,
            int page,
            CancellationToken cancellationToken)
        {
            var existing = (await context.Discover.GetSectionsAsync(source, cancellationToken))
                .FirstOrDefault(row => string.Equals(row.SectionId, section.Id, StringComparison.Ordinal));
            var now = _time.GetUtcNow().UtcDateTime;

            var result = await context.Reader.GetPageAsync(section, page, cancellationToken);
            if (!result.Succeeded)
            {
                await context.Discover.SaveSectionAsync(
                    source,
                    section.Id,
                    section.Name,
                    existing?.PagesFetched ?? 0,
                    existing?.TotalItems,
                    existing?.Exhausted ?? false,
                    result.Reason,
                    now,
                    cancellationToken);
                return false;
            }

            var itemsPerPage = Math.Max(result.Items.Count, 1);
            var rows = result.Items
                .Select((item, index) => new DiscoverItem
                {
                    Source = source,
                    SectionId = section.Id,
                    ExternalId = item.ExternalId,
                    Rank = ((page - 1) * itemsPerPage) + index + 1,
                    Page = page,
                    FirstSeenAt = now,
                    UpdatedAt = now,
                    PostedAt = item.PostedAt,
                    SourceUrl = item.SourceUrl,
                    SourceNote = item.SourceNote,
                    Enriched = item.Resolved,
                    Book = item.Book
                })
                .ToList();

            // Re-reading a page only ever adds. A chart page used to be treated as a set
            // of positions and replaced wholesale, but a row's identity is its ASIN and
            // a chart reorders between reads: a book that moved from page two to page
            // three was deleted when page two was re-read and added back as a new row
            // when page three was, losing when it was first seen and the Audible lookup
            // that had already been spent on it. The rank and page of a book still in
            // the chart are updated in place; one that has left keeps its last known
            // place, and its UpdatedAt stops advancing, which is what says so.
            var added = await context.Discover.UpsertItemsAsync(
                source, section.Id, rows, cancellationToken);

            if (source == DiscoverSources.AbookLink)
            {
                await context.Discover.TrimSectionAsync(source, section.Id, BoardRetention, cancellationToken);
            }

            await context.Discover.SaveSectionAsync(
                source,
                section.Id,
                section.Name,
                Math.Max(existing?.PagesFetched ?? 0, page),
                result.Total ?? existing?.TotalItems,
                !result.HasMore,
                null,
                now,
                cancellationToken);

            logger.LogDebug(
                "Discovery: {Source} {Section} page {Page} gave {Count} item(s), {Added} new",
                source, section.Name, page, rows.Count, added);
            return true;
        }

        private async Task<bool> ResolveAsync(
            SourceContext context,
            DiscoverItem item,
            CancellationToken cancellationToken)
        {
            var resolved = await context.Resolver.ResolveAsync(item.Book, cancellationToken);
            var book = resolved is null ? item.Book : DiscoverMapping.Merge(item.Book, resolved);

            // Marked looked-up either way: a title the catalog does not know is not
            // worth searching for again every cycle.
            return await context.Discover.ResolveItemAsync(
                item.Id, book, _time.GetUtcNow().UtcDateTime, cancellationToken);
        }

        private static SourceContext Resolve(IServiceScope scope, string source)
        {
            var provider = scope.ServiceProvider;
            var reader = provider.GetServices<IDiscoverSourceReader>()
                .FirstOrDefault(candidate => string.Equals(candidate.Source, source, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"No reader is registered for discovery source '{source}'.");

            var configuration = provider.GetRequiredService<IConfigurationService>();
            return new SourceContext(
                reader,
                provider.GetRequiredService<IDiscoverRepository>(),
                provider.GetRequiredService<IDiscoverMetadataResolver>(),
                async ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    var settings = await configuration.GetApplicationSettingsAsync();
                    return DiscoverSettings.Read(settings.DiscoverSettingsJson, source);
                });
        }

        private sealed record SourceContext(
            IDiscoverSourceReader Reader,
            IDiscoverRepository Discover,
            IDiscoverMetadataResolver Resolver,
            Func<CancellationToken, Task<DiscoverSelection>> SelectionAsync);

        private sealed record WorkItem(DiscoverSelectedSection? Section, int Page, DiscoverItem? Item = null);
    }
}
