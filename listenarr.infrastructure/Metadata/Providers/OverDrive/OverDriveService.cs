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
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Listenarr.Application.Search.Contracts;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Metadata.Providers.OverDrive
{
    /// <summary>
    /// Library lending catalogues, read through OverDrive's public search.
    ///
    /// <para>
    /// No key and no account: the endpoint a library's own website calls answers anyone.
    /// A library code is still required by the route, so one large public system is used
    /// as the window onto the shared catalogue - the records are the publishers', not
    /// that library's, and which system is asked changes only what it happens to license.
    /// </para>
    /// </summary>
    public sealed class OverDriveService(HttpClient httpClient, ILogger<OverDriveService> logger) : IOverDriveService
    {
        /// <summary>
        /// The library whose catalogue is searched. Los Angeles Public Library is large,
        /// open to anyone, and lends audiobooks widely; nothing here is borrowed, only read.
        /// </summary>
        private const string Library = "lapl";

        private const int MaxResults = 20;

        public async Task<IReadOnlyList<OverDriveEdition>> SearchAsync(
            string? title,
            string? author,
            CancellationToken cancellationToken = default)
        {
            var query = string.Join(' ', new[] { title, author }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim()));
            if (query.Length == 0)
            {
                return [];
            }

            var url = $"v2/libraries/{Library}/media"
                + $"?query={Uri.EscapeDataString(query)}"
                + $"&format=audiobook-overdrive&perPage={MaxResults}";

            try
            {
                using var response = await httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogDebug("OverDrive answered {Status} for {Query}", response.StatusCode, query);
                    return [];
                }

                var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                if (!payload.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                var editions = new List<OverDriveEdition>();
                foreach (var item in items.EnumerateArray())
                {
                    var edition = Map(item);
                    // Only editions that name a reader are worth returning: the narrator is
                    // the whole reason to ask a second catalogue.
                    if (edition is { Narrators.Count: > 0 })
                    {
                        editions.Add(edition);
                    }
                }

                return editions;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogDebug(ex, "OverDrive could not be asked about {Query}", query);
                return [];
            }
        }

        private static OverDriveEdition? Map(JsonElement item)
        {
            var id = Text(item, "id");
            var title = Text(item, "title");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var authors = Credited(item, "Author");
            var narrators = Credited(item, "Narrator");

            string? publisher = null;
            if (item.TryGetProperty("publisher", out var publisherElement)
                && publisherElement.ValueKind == JsonValueKind.Object)
            {
                publisher = Text(publisherElement, "name");
            }

            return new OverDriveEdition(
                id!,
                title!,
                authors,
                narrators,
                publisher,
                RuntimeMinutes(item),
                Year(Text(item, "publishDate")),
                CoverUrl(item));
        }

        private static IReadOnlyList<string> Credited(JsonElement item, string role)
        {
            if (!item.TryGetProperty("creators", out var creators) || creators.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return creators
                .EnumerateArray()
                .Where(creator => string.Equals(Text(creator, "role"), role, StringComparison.OrdinalIgnoreCase))
                .Select(creator => Text(creator, "name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
                .ToList();
        }

        /// <summary>The recording's length, which the catalogue writes as "08:20:08".</summary>
        private static int? RuntimeMinutes(JsonElement item)
        {
            if (!item.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var format in formats.EnumerateArray())
            {
                var duration = Text(format, "duration");
                if (string.IsNullOrWhiteSpace(duration))
                {
                    continue;
                }

                var parts = duration.Split(':');
                if (parts.Length >= 2
                    && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
                    && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
                {
                    var total = (hours * 60) + minutes;
                    if (total > 0)
                    {
                        return total;
                    }
                }
            }

            return null;
        }

        private static string? CoverUrl(JsonElement item)
        {
            if (!item.TryGetProperty("covers", out var covers) || covers.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var size in new[] { "cover510Wide", "cover300Wide", "cover150Wide" })
            {
                if (covers.TryGetProperty(size, out var cover) && cover.ValueKind == JsonValueKind.Object)
                {
                    var href = Text(cover, "href");
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        return href;
                    }
                }
            }

            return null;
        }

        private static string? Year(string? date) =>
            date is { Length: >= 4 } && int.TryParse(date[..4], out _) ? date[..4] : null;

        private static string? Text(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.ToString(),
                    _ => null
                }
                : null;
    }
}
