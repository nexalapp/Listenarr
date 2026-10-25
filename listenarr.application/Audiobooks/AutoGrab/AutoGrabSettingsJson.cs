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

namespace Listenarr.Application.Audiobooks.AutoGrab
{
    /// <summary>
    /// Reads and writes the auto-grab choice, which lives in one settings column.
    ///
    /// <para>
    /// It is stored rather than held in the browser because the grabbing happens in a
    /// background worker with no page open; the page is only the editor for it.
    /// </para>
    /// <para>
    /// Section ids are validated here, as they are for the discovery tabs: they end up
    /// addressing a chart or a board, and anything but digits is refused rather than
    /// guarded at every use.
    /// </para>
    /// </summary>
    public static partial class AutoGrabSettingsJson
    {
        [GeneratedRegex(@"^[0-9]{1,24}$")]
        private static partial Regex SectionId();

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static AutoGrabSettings Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return AutoGrabSettings.Off;
            }

            try
            {
                var stored = JsonSerializer.Deserialize<Stored>(json, SerializerOptions);
                return stored is null ? AutoGrabSettings.Off : Sanitize(new AutoGrabSettings(
                    stored.Enabled ?? false,
                    stored.PerRun ?? AutoGrabSettings.DefaultPerRun,
                    stored.IntervalMinutes ?? AutoGrabSettings.DefaultIntervalMinutes,
                    (stored.Sources ?? [])
                        .Select(source => new AutoGrabSource(
                            source.Kind ?? string.Empty, source.SectionId, source.Enabled ?? true))
                        .ToList()));
            }
            catch (JsonException)
            {
                // A column that cannot be read means "not configured", not a broken page.
                return AutoGrabSettings.Off;
            }
        }

        public static string Write(AutoGrabSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var clean = Sanitize(settings);
            return JsonSerializer.Serialize(
                new Stored
                {
                    Enabled = clean.Enabled,
                    PerRun = clean.PerRun,
                    IntervalMinutes = clean.IntervalMinutes,
                    Sources = clean.Sources
                        .Select(source => new StoredSource
                        {
                            Kind = source.Kind,
                            SectionId = source.SectionId,
                            Enabled = source.Enabled
                        })
                        .ToList()
                },
                SerializerOptions);
        }

        /// <summary>
        /// What the choice is once the unusable parts are dropped: unknown lists,
        /// sections that are not ids, a section on a tab that has none, duplicates, and
        /// counts outside the allowed range.
        /// </summary>
        public static AutoGrabSettings Sanitize(AutoGrabSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sources = new List<AutoGrabSource>();
            foreach (var source in settings.Sources ?? [])
            {
                var kind = (source?.Kind ?? string.Empty).Trim().ToLowerInvariant();
                if (!AutoGrabSources.IsKnown(kind) || sources.Count >= AutoGrabSettings.MaxSources)
                {
                    continue;
                }

                var section = (source!.SectionId ?? string.Empty).Trim();
                if (AutoGrabSources.TakesSection(kind))
                {
                    if (!SectionId().IsMatch(section))
                    {
                        continue;
                    }
                }
                else
                {
                    section = string.Empty;
                }

                var clean = new AutoGrabSource(kind, section.Length > 0 ? section : null, source.Enabled);
                if (seen.Add(clean.Key))
                {
                    sources.Add(clean);
                }
            }

            return new AutoGrabSettings(
                settings.Enabled,
                settings.PerRun <= 0
                    ? AutoGrabSettings.DefaultPerRun
                    : Math.Min(settings.PerRun, AutoGrabSettings.MaxPerRun),
                settings.IntervalMinutes <= 0
                    ? AutoGrabSettings.DefaultIntervalMinutes
                    : Math.Min(settings.IntervalMinutes, 24 * 60),
                sources);
        }

        private sealed class Stored
        {
            public bool? Enabled { get; set; }

            public int? PerRun { get; set; }

            public int? IntervalMinutes { get; set; }

            public List<StoredSource>? Sources { get; set; }
        }

        private sealed class StoredSource
        {
            public string? Kind { get; set; }

            public string? SectionId { get; set; }

            public bool? Enabled { get; set; }
        }
    }
}
