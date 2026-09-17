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
using System.Text.Json.Serialization;

namespace Listenarr.Domain.Audiobooks
{
    /// <summary>One spelling of an author's name that should be stored as another.</summary>
    public sealed record AuthorAlias(
        [property: JsonPropertyName("variant")] string Variant,
        [property: JsonPropertyName("canonical")] string Canonical);

    /// <summary>
    /// The author-name aliases setting: spellings a provider may send, and the one the
    /// library keeps.
    ///
    /// Audible itself is inconsistent - "B. V. Larson" on one title and "B.V. Larson" on
    /// the next - and a library groups by the exact string, so one author becomes two
    /// folders and two author pages. The list is the operator's, applied to every author
    /// list as it is saved; nothing is inferred from spacing or punctuation. Variants
    /// match case-insensitively; the canonical spelling is written exactly as given.
    /// </summary>
    public static class AuthorAliases
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static string EmptyJson => "[]";

        public static IReadOnlyList<AuthorAlias> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<List<AuthorAlias>>(json, Json) ?? [];
                return
                [
                    .. parsed
                        .Where(alias => !string.IsNullOrWhiteSpace(alias.Variant)
                            && !string.IsNullOrWhiteSpace(alias.Canonical))
                        .Select(alias => new AuthorAlias(alias.Variant.Trim(), alias.Canonical.Trim()))
                ];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public static string ToJson(IEnumerable<AuthorAlias> aliases) =>
            JsonSerializer.Serialize(aliases.ToList(), Json);

        /// <summary>
        /// The names with every aliased spelling replaced, duplicates that the replacement
        /// produced collapsed, order otherwise kept. Returns the same instance when nothing
        /// changes, so a caller can tell whether there is anything to save.
        /// </summary>
        public static List<string> Apply(List<string>? authors, IReadOnlyList<AuthorAlias> aliases)
        {
            if (authors == null || authors.Count == 0 || aliases.Count == 0)
            {
                return authors ?? [];
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var alias in aliases)
            {
                map.TryAdd(alias.Variant, alias.Canonical);
            }

            var changed = false;
            var result = new List<string>(authors.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var author in authors)
            {
                var name = author;
                if (author != null && map.TryGetValue(author.Trim(), out var canonical) && canonical != author)
                {
                    name = canonical;
                    changed = true;
                }

                if (name == null)
                {
                    result.Add(name!);
                    continue;
                }

                if (seen.Add(name))
                {
                    result.Add(name);
                }
                else
                {
                    changed = true;
                }
            }

            return changed ? result : authors;
        }
    }
}
