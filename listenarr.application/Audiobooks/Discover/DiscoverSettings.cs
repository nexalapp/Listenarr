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
using System.Text.RegularExpressions;
using Listenarr.Domain.Audiobooks.Discover;

namespace Listenarr.Application.Audiobooks.Discover
{
    /// <summary>
    /// Reads and writes the discovery selection, which lives in one settings column
    /// keyed by source.
    ///
    /// <para>
    /// Section ids and slugs are built into the URLs the fetchers request, so they are
    /// validated here rather than where they are used: an id is digits and a slug is a
    /// lower-case URL segment, which is what both sources issue, and nothing else is
    /// stored. That keeps a crafted settings value from steering a server-side request
    /// somewhere else (CWE-918).
    /// </para>
    /// </summary>
    public static partial class DiscoverSettings
    {
        /// <summary>
        /// Audible's chart ends at 100 books, which is five pages, so five is the whole
        /// of it. A forum board has no end; five pages is a fortnight or so of posts.
        /// </summary>
        public const int DefaultPagesPerSection = 5;

        public const int MaxPagesPerSection = 50;

        /// <summary>No source offers this many sections; the cap is a guard, not a policy.</summary>
        public const int MaxSections = 40;

        private const int MaxNameLength = 128;

        [GeneratedRegex(@"^[0-9]{1,24}$")]
        private static partial Regex SectionId();

        [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,99}$")]
        private static partial Regex Slug();

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static DiscoverSelection Read(string? json, string source)
        {
            var all = Parse(json);
            return all.TryGetValue(source, out var stored)
                ? Sanitize(stored)
                : DiscoverSelection.Empty(DefaultPagesPerSection);
        }

        /// <summary>
        /// The stored document with one source replaced. Other sources are preserved
        /// untouched, so two tabs saving in turn do not erase one another.
        /// </summary>
        public static string Write(string? json, string source, DiscoverSelection selection)
        {
            ArgumentNullException.ThrowIfNull(selection);
            if (!DiscoverSources.IsKnown(source))
            {
                throw new ArgumentException($"Unknown discovery source '{source}'.", nameof(source));
            }

            var all = Parse(json);
            all[source] = ToStored(Sanitize(selection));
            return JsonSerializer.Serialize(all, SerializerOptions);
        }

        /// <summary>
        /// What a selection is once the unusable parts are dropped: ids that are not ids,
        /// names that are blank, duplicates, and a page count outside the allowed range.
        /// </summary>
        public static DiscoverSelection Sanitize(DiscoverSelection selection)
        {
            ArgumentNullException.ThrowIfNull(selection);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sections = new List<DiscoverSelectedSection>();
            foreach (var section in selection.Sections ?? [])
            {
                var id = (section?.Id ?? string.Empty).Trim();
                if (!SectionId().IsMatch(id) || !seen.Add(id) || sections.Count >= MaxSections)
                {
                    continue;
                }

                var slug = (section!.Slug ?? string.Empty).Trim().ToLowerInvariant();
                sections.Add(new DiscoverSelectedSection(
                    id,
                    CleanName(section.Name) is { Length: > 0 } name ? name : id,
                    Slug().IsMatch(slug) ? slug : null));
            }

            var pages = selection.PagesPerSection <= 0
                ? DefaultPagesPerSection
                : Math.Min(selection.PagesPerSection, MaxPagesPerSection);

            return new DiscoverSelection(sections, pages);
        }

        /// <summary>
        /// A section name as it is worth storing: a source's own label, with control
        /// characters and runs of whitespace taken out so it cannot carry a line break
        /// into a log or a field into the UI it was not meant to reach.
        /// </summary>
        private static string CleanName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var cleaned = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return cleaned.Length > MaxNameLength ? cleaned[..MaxNameLength] : cleaned;
        }

        private static Dictionary<string, StoredSelection> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, StoredSelection>(StringComparer.Ordinal);
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, StoredSelection>>(json, SerializerOptions);
                return parsed is null
                    ? new Dictionary<string, StoredSelection>(StringComparer.Ordinal)
                    : new Dictionary<string, StoredSelection>(parsed, StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                // A settings value that cannot be read is treated as "nothing chosen"
                // rather than throwing: the page must still load.
                return new Dictionary<string, StoredSelection>(StringComparer.Ordinal);
            }
        }

        private static DiscoverSelection Sanitize(StoredSelection stored) =>
            Sanitize(new DiscoverSelection(
                (stored.Sections ?? [])
                    .Select(s => new DiscoverSelectedSection(s.Id ?? string.Empty, s.Name ?? string.Empty, s.Slug))
                    .ToList(),
                stored.PagesPerSection ?? DefaultPagesPerSection));

        private static StoredSelection ToStored(DiscoverSelection selection) => new()
        {
            Sections = selection.Sections
                .Select(s => new StoredSection { Id = s.Id, Name = s.Name, Slug = s.Slug })
                .ToList(),
            PagesPerSection = selection.PagesPerSection
        };

        private sealed class StoredSelection
        {
            public List<StoredSection>? Sections { get; set; }

            public int? PagesPerSection { get; set; }
        }

        private sealed class StoredSection
        {
            public string? Id { get; set; }

            public string? Name { get; set; }

            public string? Slug { get; set; }
        }
    }
}
