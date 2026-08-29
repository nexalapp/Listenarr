/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Globalization;
using System.Text.Json;

namespace Listenarr.Infrastructure.Ffmpeg.Metadata
{
    internal static class FfprobeTagMetadataMapper
    {
        public static void Apply(AudioMetadata metadata, JsonElement tags)
        {
            metadata.Title = FirstNonEmpty(metadata.Title, GetTag(tags, "title", "TITLE"));
            metadata.Artist = FirstNonEmpty(metadata.Artist, GetTag(tags, "artist", "ARTIST"));
            metadata.Album = FirstNonEmpty(metadata.Album, GetTag(tags, "album", "ALBUM"));
            metadata.AlbumArtist = FirstNonEmpty(metadata.AlbumArtist, GetTag(tags, "album_artist", "ALBUM_ARTIST", "album artist"));

            metadata.TrackNumber ??= ParseNumericTag(tags, "track", "TRACK", "tracknumber", "TRACKNUMBER");
            metadata.DiscNumber ??= ParseNumericTag(tags, "disc", "DISC", "discnumber", "DISCNUMBER");
            metadata.Year ??= ParseNumericTag(tags, "date", "DATE", "year", "YEAR");

            // Embedded audiobook identifiers. Audible/OpenAudible-tagged m4b files carry
            // the ASIN (and sometimes an ISBN) in these tags; reading them here lets a scan
            // adopt the identifier onto a bare audiobook and auto-populate its metadata.
            if (string.IsNullOrWhiteSpace(metadata.Asin))
            {
                metadata.Asin = GetTag(tags, "ASIN", "asin", "AUDIBLE_ASIN", "audible_asin");
            }

            if (string.IsNullOrWhiteSpace(metadata.Isbn))
            {
                metadata.Isbn = GetTag(tags, "ISBN", "isbn");
            }

            ApplyDescriptiveTags(metadata, tags);
        }

        /// <summary>
        /// Reads the descriptive tags an audiobook file carries beyond the basic music
        /// fields. These are what a manual import falls back on when no catalogue match
        /// exists, so the file itself has to supply the blurb, narrator and series.
        /// </summary>
        private static void ApplyDescriptiveTags(AudioMetadata metadata, JsonElement tags)
        {
            metadata.Genre = FirstNonEmpty(metadata.Genre, GetTag(tags, "genre", "GENRE"));

            // "description" is the atom Plex and Prologue read; "comment" carries the same
            // blurb in most taggers and is the more common of the two in the wild.
            metadata.Description ??= GetTag(tags, "description", "DESCRIPTION", "comment", "COMMENT");
            metadata.Subtitle ??= GetTag(tags, "subtitle", "SUBTITLE", "TIT3");

            // Audiobook taggers put the narrator in the composer field: there is no
            // narrator atom, and the reader is the closest analogue to a composer.
            metadata.Narrator ??= GetTag(tags, "composer", "COMPOSER", "narrator", "NARRATOR");
            metadata.Publisher ??= GetTag(tags, "publisher", "PUBLISHER", "label", "LABEL");
            metadata.Language ??= GetTag(tags, "language", "LANGUAGE");

            metadata.Series ??= GetTag(tags, "SERIES", "series", "show", "SHOW");
            metadata.SeriesPosition ??= ParseSeriesPosition(
                GetTag(tags, "SERIES-PART", "series-part", "SERIES_PART", "MOVEMENT", "movement"));
        }

        /// <summary>
        /// A series position is written as a decimal string that always uses '.' as its
        /// separator, so it is parsed with the invariant culture rather than the server's.
        /// Non-numeric positions ("1-4" for an omnibus) have no decimal form and are left
        /// unset rather than being coerced into a wrong number.
        /// </summary>
        private static decimal? ParseSeriesPosition(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return decimal.TryParse(
                raw.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null;
        }

        private static string FirstNonEmpty(params string?[] candidates)
        {
            foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
            {
                return candidate!;
            }

            return string.Empty;
        }

        private static string? GetTag(JsonElement tags, params string[] names)
        {
            return names
                .Select(name => TryGetTagValue(tags, name, out var value) ? value : null)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
                ?.Trim();
        }

        private static int? ParseNumericTag(JsonElement tags, params string[] names)
        {
            var raw = GetTag(tags, names);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var token = raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? raw;
            var match = System.Text.RegularExpressions.Regex.Match(token, @"\d+");
            return match.Success && int.TryParse(match.Value, out var parsed) ? parsed : null;
        }

        private static bool TryGetTagValue(JsonElement tags, string name, out string? value)
        {
            if (tags.TryGetProperty(name, out var direct) && direct.ValueKind == JsonValueKind.String)
            {
                value = direct.GetString();
                return true;
            }

            foreach (var property in tags.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    value = property.Value.GetString();
                    return true;
                }
            }

            value = null;
            return false;
        }
    }
}
