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
namespace Listenarr.Application.Metadata.Languages
{
    /// <summary>
    /// One reading of a language filter for every place that applies one.
    ///
    /// A filter is a language name ("english"), a comma-separated list of them
    /// ("english,german") - which is how the library-languages setting travels through
    /// the same query parameter the single language always used - or "all" / empty,
    /// meaning no filter. Names are normalised through the same aliases the catalogs use.
    /// </summary>
    public static class LanguageFilter
    {
        /// <summary>The languages a filter admits, or null when it admits everything.</summary>
        public static IReadOnlySet<string>? Parse(string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return null;
            }

            var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(part, "all", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var normalized = AuthorCatalogMapping.NormalizeLanguage(part);
                if (normalized != null)
                {
                    languages.Add(normalized);
                }
            }

            return languages.Count == 0 ? null : languages;
        }

        public static bool IsUnfiltered(string? filter) => Parse(filter) == null;

        /// <summary>
        /// Whether a result in <paramref name="language"/> passes the filter.
        /// <paramref name="acceptUnknown"/> decides a result with no language recorded:
        /// a catalog listing keeps it, a strict match drops it.
        /// </summary>
        public static bool Matches(string? filter, string? language, bool acceptUnknown)
        {
            var admitted = Parse(filter);
            if (admitted == null)
            {
                return true;
            }

            var normalized = AuthorCatalogMapping.NormalizeLanguage(language);
            return normalized == null ? acceptUnknown : admitted.Contains(normalized);
        }

        /// <summary>The filter as the settings define it: the library languages, else the default search language.</summary>
        public static string? FromSettings(ApplicationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var explicitList = Parse(ParseJsonList(settings.LibraryLanguagesJson));
            if (explicitList != null)
            {
                return string.Join(',', explicitList.OrderBy(language => language, StringComparer.Ordinal));
            }

            return Parse(settings.DefaultSearchLanguage) == null ? null : settings.DefaultSearchLanguage;
        }

        private static string? ParseJsonList(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                return list == null ? null : string.Join(',', list);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }
}
