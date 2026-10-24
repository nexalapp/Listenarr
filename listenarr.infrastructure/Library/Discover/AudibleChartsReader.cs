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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Discover
{
    /// <summary>
    /// Reads Audible's bestseller charts.
    ///
    /// <para>
    /// The chart page is asked for the ASINs in its order and nothing else; everything
    /// shown on a card then comes from the catalog API, in one request per page of
    /// twenty. That is one scrape of a layout that changes, and one call to an interface
    /// that does not, for twenty books.
    /// </para>
    /// <para>
    /// A chart is a hundred books in five pages and a sixth page repeats the fifth, so
    /// the end is found from the "21 - 40 of 100" line on the page rather than by
    /// reading until something looks familiar.
    /// </para>
    /// </summary>
    public sealed class AudibleChartsReader(
        HttpClient httpClient,
        AudibleService audible,
        IConfigurationService configuration,
        ILogger<AudibleChartsReader> logger) : IDiscoverSourceReader
    {
        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

        public string Source => DiscoverSources.AudibleCharts;

        public async Task<string?> UnavailableReasonAsync(CancellationToken cancellationToken = default)
        {
            var settings = await configuration.GetApplicationSettingsAsync();
            return settings.EnableAudibleSearch
                ? null
                : "Audible is turned off in Settings → Search, and the charts come from Audible.";
        }

        public async Task<IReadOnlyList<DiscoverSectionOption>> ListSectionsAsync(
            CancellationToken cancellationToken = default)
        {
            var region = await RegionAsync();
            var html = await GetAsync($"{AudibleRequestHelper.GetBaseUrl(region)}/charts/best", cancellationToken);
            if (html is null)
            {
                return [];
            }

            return AudibleChartsParser.ParseCategories(html)
                .Select(category => new DiscoverSectionOption(category.NodeId, category.Name, category.Slug))
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<DiscoverPage> GetPageAsync(
            DiscoverSelectedSection section,
            int page,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(section);

            var region = await RegionAsync();
            var slug = section.Slug ?? await ResolveSlugAsync(section.Id, cancellationToken);
            if (slug is null)
            {
                return DiscoverPage.Failed(
                    $"Audible does not list a chart for category {section.Id} any more.");
            }

            var url =
                $"{AudibleRequestHelper.GetBaseUrl(region)}/charts/best/{slug}/{section.Id}" +
                (page > 1 ? $"?page={page}" : string.Empty);

            var html = await GetAsync(url, cancellationToken);
            if (html is null)
            {
                return DiscoverPage.Failed("Audible's charts could not be read.");
            }

            var parsed = AudibleChartsParser.ParsePage(html);
            if (parsed.Asins.Count == 0)
            {
                // An empty page past the end is the chart finishing; an empty first page
                // is a layout we no longer understand, and must not read as "no books".
                return page > 1
                    ? new DiscoverPage(true, [], parsed.Total, false, null)
                    : DiscoverPage.Failed("Audible's chart page listed no books.");
            }

            var metadata = await audible.GetBooksByAsinsAsync(parsed.Asins, region);
            var byAsin = metadata
                .Where(book => !string.IsNullOrWhiteSpace(book.Asin))
                .GroupBy(book => book.Asin!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            // The chart's own numbering, taken from the page's "21 - 40 of 100" line so
            // a page read out of order still ranks its books where the chart put them.
            var first = parsed.Last.HasValue
                ? parsed.Last.Value - parsed.Asins.Count + 1
                : ((page - 1) * parsed.Asins.Count) + 1;

            var items = new List<DiscoverPageItem>();
            for (var index = 0; index < parsed.Asins.Count; index++)
            {
                var asin = parsed.Asins[index];
                if (!byAsin.TryGetValue(asin, out var book))
                {
                    logger.LogDebug("Audible charts: no catalog record for {Asin}", asin);
                    continue;
                }

                var mapped = DiscoverMapping.FromSearchResult(book, asin);
                items.Add(new DiscoverPageItem(
                    asin,
                    mapped,
                    PostedAt: null,
                    SourceUrl: mapped.Link,
                    SourceNote: $"#{first + index} in {section.Name}",
                    Resolved: true));
            }

            var hasMore = parsed.Total.HasValue && parsed.Last.HasValue
                ? parsed.Last.Value < parsed.Total.Value
                : parsed.Asins.Count > 0;

            return new DiscoverPage(true, items, parsed.Total, hasMore, null);
        }

        private async Task<string> RegionAsync()
        {
            var settings = await configuration.GetApplicationSettingsAsync();
            return AudibleRequestHelper.NormalizeRegion(settings.DefaultSearchRegion ?? "us");
        }

        /// <summary>
        /// The URL segment a chart is addressed by, for a selection stored without one.
        /// </summary>
        private async Task<string?> ResolveSlugAsync(string nodeId, CancellationToken cancellationToken)
        {
            var sections = await ListSectionsAsync(cancellationToken);
            return sections.FirstOrDefault(section => string.Equals(section.Id, nodeId, StringComparison.Ordinal))?.Slug;
        }

        private async Task<string?> GetAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                // Validated and redirect-checked like every other outbound request, so a
                // stored category cannot send this at something that is not Audible.
                var (response, _) = await OutboundRequestSecurity.SendWithValidatedRedirectsAsync(
                    target =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, target);
                        request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
                        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
                        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                        return request;
                    },
                    new Uri(url),
                    httpClient,
                    logger,
                    cancellationToken: cancellationToken);

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning(
                            "Audible charts returned HTTP {StatusCode} for {Url}", (int)response.StatusCode, url);
                        return null;
                    }

                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Audible charts could not be read from {Url}", url);
                return null;
            }
        }
    }
}
