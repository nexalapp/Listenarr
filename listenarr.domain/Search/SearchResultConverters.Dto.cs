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

namespace Listenarr.Domain.Search
{
    public static partial class SearchResultConverters
    {
        public static IndexerResultDto ToIndexerResultDto(IndexerSearchResult result)
        {
            var dto = new IndexerResultDto
            {
                Guid = !string.IsNullOrWhiteSpace(result.ResultUrl) ? result.ResultUrl : (!string.IsNullOrWhiteSpace(result.TorrentUrl) ? result.TorrentUrl : result.Id),
                Size = result.Size,
                Files = result.Files,
                Grabs = result.Grabs,
                IndexerId = result.IndexerId,
                Indexer = result.Source,
                Title = result.Title ?? string.Empty,
                PublishDate = result.PublishedDate,
                DownloadUrl = !string.IsNullOrWhiteSpace(result.TorrentUrl) ? result.TorrentUrl : (!string.IsNullOrWhiteSpace(result.NzbUrl) ? result.NzbUrl : null),
                InfoUrl = result.ResultUrl,
                Seeders = result.Seeders,
                Leechers = result.Leechers,
                Protocol = !string.IsNullOrWhiteSpace(result.DownloadType) ? result.DownloadType.ToLowerInvariant() : null,
                FileName = result.TorrentFileName,
                DownloadReference = result.DownloadReference,
                FileType = string.IsNullOrWhiteSpace(result.Format) ? null : result.Format,
                Language = NormalizeLanguage(result.Language)
            };

            // Derive age fields from publishDate if available
            if (!string.IsNullOrWhiteSpace(dto.PublishDate) && DateTimeOffset.TryParse(dto.PublishDate, out var dtoPub))
            {
                var ageSpan = DateTimeOffset.UtcNow - dtoPub;
                dto.Age = (int)Math.Floor(ageSpan.TotalDays);
                dto.AgeHours = ageSpan.TotalHours;
                dto.AgeMinutes = ageSpan.TotalMinutes;
            }

            // SortTitle: normalized lower-case, remove punctuation
            dto.SortTitle = System.Text.RegularExpressions.Regex.Replace(dto.Title?.ToLowerInvariant() ?? string.Empty, "[^a-z0-9 ]", "").Trim();

            // IndexerFlags and Categories: best-effort mapping (not always present in result); keep empty lists if not available
            dto.IndexerFlags = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Category))
            {
                // Keep a simple category object with name
                dto.Categories.Add(new { id = 0, name = result.Category, subCategories = new object[0] });
            }

            return dto;
        }
    }
}
