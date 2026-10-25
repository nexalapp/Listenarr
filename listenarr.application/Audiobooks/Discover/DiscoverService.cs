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
using Listenarr.Application.Audiobooks.Suggestions;
using Listenarr.Domain.Audiobooks.Discover;

namespace Listenarr.Application.Audiobooks.Discover
{
    /// <summary>
    /// The two discovery tabs, answered from the cache.
    ///
    /// <para>
    /// A book already on the shelf is not a discovery, so held books are counted and
    /// left out rather than listed; so is anything the user has said no to on the
    /// Suggested page, because an ignored book is ignored wherever it turns up.
    /// </para>
    /// </summary>
    public sealed class DiscoverService(
        IDiscoverRepository discover,
        IAudiobookRepository repository,
        IConfigurationService configuration,
        IDiscoverRefreshService refresh,
        DiscoverSectionCatalogCache sectionCatalog,
        IEnumerable<IDiscoverSourceReader> readers) : IDiscoverService
    {
        public async Task<DiscoverFeed> GetFeedAsync(string source, CancellationToken cancellationToken = default)
        {
            var reader = ReaderFor(source);
            var settings = await configuration.GetApplicationSettingsAsync();
            var selection = DiscoverSettings.Read(settings.DiscoverSettingsJson, source);
            var unavailable = await reader.UnavailableReasonAsync(cancellationToken);

            if (selection.Sections.Count == 0)
            {
                return new DiscoverFeed(
                    source, [], selection.PagesPerSection, unavailable is null, unavailable, refresh.StatusFor(source));
            }

            var selectedIds = selection.Sections.Select(section => section.Id).ToList();
            var sections = (await discover.GetSectionsAsync(source, cancellationToken))
                .ToDictionary(section => section.SectionId, StringComparer.Ordinal);
            var items = await discover.GetItemsAsync(source, selectedIds, cancellationToken);

            var held = await BuildHeldAsync(settings, cancellationToken);
            var bySection = items
                .GroupBy(item => item.SectionId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            var feeds = new List<DiscoverSectionFeed>();
            foreach (var selected in selection.Sections)
            {
                sections.TryGetValue(selected.Id, out var state);
                bySection.TryGetValue(selected.Id, out var rows);

                var ordered = Order(source, rows ?? []);
                var books = new List<DiscoverEntry>();
                var heldCount = 0;
                var rank = 0;

                foreach (var item in ordered)
                {
                    rank++;
                    var book = item.Book;
                    if (!held.SpeaksLanguage(book.Language))
                    {
                        continue;
                    }

                    if (held.Contains(book.Asin, book.Isbn, book.Title, book.Authors)
                        || held.CoversSameStory(book.Title, book.Authors, book.Series, book.SeriesNumber))
                    {
                        heldCount++;
                        continue;
                    }

                    if (held.Ignored(book.Asin, book.Title, book.Authors))
                    {
                        continue;
                    }

                    books.Add(new DiscoverEntry(
                        // The chart's own place, not the place in what is left: a book
                        // ranked third stays third when the two above it are on the shelf.
                        source == DiscoverSources.AudibleCharts ? item.Rank : rank,
                        item.PostedAt,
                        item.SourceUrl,
                        item.SourceNote,
                        item.Enriched,
                        DiscoverMapping.ToSuggestedBook(book)));
                }

                feeds.Add(new DiscoverSectionFeed(
                    selected.Id,
                    state?.SectionName is { Length: > 0 } name ? name : selected.Name,
                    state?.PagesFetched ?? 0,
                    state?.TotalItems,
                    state?.LastFetchedAt,
                    state?.LastError,
                    heldCount,
                    books));
            }

            return new DiscoverFeed(
                source, feeds, selection.PagesPerSection, unavailable is null, unavailable, refresh.StatusFor(source));
        }

        public async Task<DiscoverSectionCatalog> GetSectionsAsync(string source, CancellationToken cancellationToken = default)
        {
            var reader = ReaderFor(source);
            var settings = await configuration.GetApplicationSettingsAsync();
            var selection = DiscoverSettings.Read(settings.DiscoverSettingsJson, source);
            var unavailable = await reader.UnavailableReasonAsync(cancellationToken);

            if (unavailable is not null)
            {
                return new DiscoverSectionCatalog(
                    source, [], selection.Sections.Select(s => s.Id).ToList(), selection.PagesPerSection, false, unavailable);
            }

            var options = await sectionCatalog.GetAsync(reader.Source, reader.ListSectionsAsync, cancellationToken);

            // A section that was chosen and has since vanished from the source's index
            // stays in the list, so unselecting it is possible and its books are explained.
            var known = options.Select(option => option.Id).ToHashSet(StringComparer.Ordinal);
            var merged = options
                .Concat(selection.Sections
                    .Where(section => !known.Contains(section.Id))
                    .Select(section => new DiscoverSectionOption(section.Id, section.Name, section.Slug)))
                .ToList();

            return new DiscoverSectionCatalog(
                source,
                merged,
                selection.Sections.Select(section => section.Id).ToList(),
                selection.PagesPerSection,
                true,
                null);
        }

        public async Task<DiscoverSelection> SaveSelectionAsync(
            string source,
            SaveDiscoverSelectionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            _ = ReaderFor(source);

            var settings = await configuration.GetApplicationSettingsAsync();
            var current = DiscoverSettings.Read(settings.DiscoverSettingsJson, source);
            var selection = DiscoverSettings.Sanitize(new DiscoverSelection(
                request.Sections ?? current.Sections,
                request.PagesPerSection ?? current.PagesPerSection));

            settings.DiscoverSettingsJson = DiscoverSettings.Write(settings.DiscoverSettingsJson, source, selection);
            await configuration.SaveApplicationSettingsAsync(settings);

            // Books cached for a section nobody follows any more are dead weight, and
            // leaving them would make reselecting it look instantly populated with
            // whatever was true months ago.
            await discover.PruneSectionsAsync(
                source,
                selection.Sections.Select(section => section.Id).ToList(),
                cancellationToken);

            return selection;
        }

        /// <summary>
        /// A chart is ordered by its own ranking; a board by when things were posted,
        /// newest first, since that is the whole point of the tab. A post with no date —
        /// a page we could not read a timestamp on — sorts by the order it was read in.
        /// </summary>
        private static List<DiscoverItem> Order(string source, List<DiscoverItem> items) =>
            source == DiscoverSources.AudibleCharts
                ? items.OrderBy(item => item.Rank).ToList()
                : items
                    .OrderByDescending(item => item.PostedAt ?? DateTime.MinValue)
                    .ThenBy(item => item.Rank)
                    .ToList();

        private async Task<HeldBooks> BuildHeldAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            var languages = LanguageFilter.Parse(LanguageFilter.FromSettings(settings));
            var aliases = AuthorAliases.Parse(settings.AuthorAliasesJson);
            var library = await repository.GetAllAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var memberships = await repository.GetAllSeriesMembershipsGroupedByAudiobookIdAsync(cancellationToken);
            var dismissals = await repository.GetSuggestionDismissalsAsync(cancellationToken);
            return new HeldBooks(
                library, memberships, dismissals.Select(dismissal => dismissal.Key), languages, aliases);
        }

        private IDiscoverSourceReader ReaderFor(string source) =>
            readers.FirstOrDefault(reader => string.Equals(reader.Source, source, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown discovery source '{source}'.", nameof(source));
    }
}
