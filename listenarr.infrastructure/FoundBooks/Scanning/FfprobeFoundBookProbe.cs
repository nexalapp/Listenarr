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
using System.Text.RegularExpressions;
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Domain.FoundBooks;

namespace Listenarr.Infrastructure.FoundBooks.Scanning
{
    /// <summary>
    /// Probes one audio file through ffprobe and keeps the handful of tags the found-book
    /// scanner reasons about. Never throws for an unreadable file: that is a verdict
    /// about the file, and it goes in the snapshot.
    /// </summary>
    public sealed partial class FfprobeFoundBookProbe(FfprobeTagReader reader) : IFoundBookProbe
    {
        [GeneratedRegex(@"\b(?:B0[A-Z0-9]{8}|[0-9][A-Z0-9]{9})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex AsinPattern();

        public async Task<FoundBookProbeSnapshot> ProbeAsync(string path, CancellationToken cancellationToken = default)
        {
            var outcome = await reader.ProbeAsync(path, cancellationToken);
            if (outcome.Tags == null)
            {
                return Failed(outcome.Error ?? "ffprobe could not read the file.");
            }

            var tags = outcome.Tags.Tags;
            var (track, trackTotal) = Track(Get(tags, "track", "tracknumber"));
            trackTotal ??= Int(Get(tags, "tracktotal", "totaltracks"));

            return new FoundBookProbeSnapshot(
                Succeeded: true,
                Error: null,
                DurationSeconds: outcome.Tags.Duration.TotalSeconds,
                ChapterCount: outcome.Tags.ChapterCount,
                TrackNumber: track,
                TrackTotal: trackTotal,
                Title: Get(tags, "title"),
                Album: Get(tags, "album"),
                Artist: Get(tags, "artist"),
                AlbumArtist: Get(tags, "album_artist", "albumartist"),
                Narrator: Get(tags, "narrator", "composer"),
                Series: Get(tags, "series", "mvnm"),
                SeriesPosition: Get(tags, "series-part", "seriespart", "part", "mvin"),
                Year: Year(Get(tags, "date", "year", "originaldate")),
                Asin: Asin(tags),
                DeclaredDurationSeconds: DeclaredLengthParser.Parse(Get(tags, "comment", "description", "synopsis")));
        }

        internal static FoundBookProbeSnapshot Failed(string error) =>
            new(false, error, 0, 0, null, null, null, null, null, null, null, null, null, null, null, null);

        private static string? Get(IReadOnlyDictionary<string, string> tags, params string[] names)
        {
            foreach (var name in names)
            {
                if (tags.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static (int? Number, int? Total) Track(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (null, null);
            }

            var parts = value.Split('/', 2);
            return (Int(parts[0]), parts.Length > 1 ? Int(parts[1]) : null);
        }

        private static int? Int(string? value) =>
            int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;

        private static string? Year(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
            {
                return null;
            }

            var head = value[..4];
            return head.All(char.IsAsciiDigit) ? head : null;
        }

        private static string? Asin(IReadOnlyDictionary<string, string> tags)
        {
            foreach (var pair in tags)
            {
                if (!pair.Key.Contains("asin", StringComparison.OrdinalIgnoreCase)
                    && !pair.Key.Contains("cdek", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = AsinPattern().Match(pair.Value);
                if (match.Success)
                {
                    return match.Value.ToUpperInvariant();
                }
            }

            return null;
        }
    }
}
