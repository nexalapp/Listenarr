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

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// What the operator chose for one run, on its way to and from the job row.
    ///
    /// A preview lets them narrow which tags are written and correct the values before
    /// they are. Both choices belong to that run rather than to the settings, so both
    /// travel on the job — and both are validated here, at the one point where a form's
    /// output becomes something a worker will write into a file.
    /// </summary>
    public sealed partial class TagQueueService
    {
        /// <summary>
        /// Persist the operator's per-run tag selection. Unknown tag names are dropped
        /// here rather than at the worker: a selection naming nothing real would silently
        /// become "write no tags", which is not what anyone asked for.
        /// </summary>
        internal static string? SerializeSelection(IReadOnlyCollection<string>? selectedTags)
        {
            if (selectedTags == null)
            {
                return null;
            }

            var known = selectedTags
                .Where(TagCatalog.IsKnown)
                .Select(tag => TagCatalog.Find(tag)!.Tag)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return JsonSerializer.Serialize(known);
        }

        /// <summary>
        /// Persist the operator's typed values. Unknown tag names are dropped and every
        /// value is sanitised here, so nothing a form produced reaches a metadata atom
        /// unexamined.
        /// </summary>
        internal static string? SerializeValues(IReadOnlyDictionary<string, string>? values)
        {
            if (values == null || values.Count == 0)
            {
                return null;
            }

            var known = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (tag, value) in values)
            {
                var definition = TagCatalog.Find(tag);
                if (definition == null)
                {
                    continue;
                }

                var sanitized = TagValue.Sanitize(value);
                if (sanitized.Length > 0)
                {
                    known[definition.Tag] = sanitized;
                }
            }

            return known.Count == 0 ? null : JsonSerializer.Serialize(known);
        }

        /// <summary>Read back stored values. Null means every value comes from its pattern.</summary>
        public static IReadOnlyDictionary<string, string>? DeserializeValues(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return values == null
                    ? null
                    : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                // Values that cannot be read must not silently fall back to the patterns
                // the operator overrode; an empty set writes nothing for those tags.
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Read back a stored selection. Null means every tag the mapping allows.</summary>
        public static IReadOnlySet<string>? DeserializeSelection(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var tags = JsonSerializer.Deserialize<List<string>>(json);
                return tags == null
                    ? null
                    : new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                // A selection that cannot be read is not a reason to write every tag the
                // operator may have deliberately excluded.
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
