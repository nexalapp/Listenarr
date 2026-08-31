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

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// Assembles the values a tag pattern resolves against.
    ///
    /// <para>
    /// The library record wins wherever it has something: it is the corrected one, and
    /// the file's tags are whatever the release happened to ship with. Where the record
    /// is silent the file's own value stands in rather than being overwritten by nothing
    /// — a book Listenarr never matched still has a blurb, and losing it to a rewrite
    /// would be a worse outcome than leaving the file alone.
    /// </para>
    /// <para>
    /// Shared by conversion and by tag writing so both resolve the same values. A
    /// conversion that disagreed with an enrichment about the same book's genre would
    /// mean the file's tags depended on which code path last touched it.
    /// </para>
    /// </summary>
    public static class AudiobookTagMetadata
    {
        /// <summary>
        /// The genre an audiobook gets when neither the library nor the file names one.
        /// Players group by genre, and "no genre" sorts an audiobook in with music.
        /// </summary>
        public const string DefaultGenre = "Audiobook";

        public static AudioMetadata Create(Audiobook audiobook, AudioMetadata? fromFile)
        {
            ArgumentNullException.ThrowIfNull(audiobook);

            var metadata = audiobook.CreateBasicAudioMetadata();

            metadata.Description = FirstNonEmpty(audiobook.Description, fromFile?.Description);
            metadata.Genre = FirstNonEmpty(metadata.Genre, fromFile?.Genre, DefaultGenre)!;
            metadata.Narrator = FirstNonEmpty(metadata.Narrator, fromFile?.Narrator);
            metadata.Publisher = FirstNonEmpty(metadata.Publisher, fromFile?.Publisher);
            metadata.Language = FirstNonEmpty(metadata.Language, fromFile?.Language);
            metadata.Subtitle = FirstNonEmpty(metadata.Subtitle, fromFile?.Subtitle);
            metadata.Asin = FirstNonEmpty(metadata.Asin, fromFile?.Asin);
            metadata.Year ??= fromFile?.Year;

            if (string.IsNullOrWhiteSpace(metadata.Artist) && fromFile != null)
            {
                metadata.Artist = fromFile.Artist;
                metadata.AlbumArtist = FirstNonEmpty(fromFile.AlbumArtist, fromFile.Artist) ?? string.Empty;
            }

            if (metadata.AllSeries == null && !string.IsNullOrWhiteSpace(fromFile?.Series))
            {
                metadata.Series = fromFile.Series;
                metadata.AllSeries =
                [
                    new SeriesReference(
                        fromFile.Series!,
                        FirstNonEmpty(
                            fromFile.SeriesPositionRaw,
                            fromFile.SeriesPosition?.ToString(CultureInfo.InvariantCulture)))
                ];
                metadata.SeriesPosition ??= fromFile.SeriesPosition;
                metadata.SeriesPositionRaw = FirstNonEmpty(
                    metadata.SeriesPositionRaw,
                    fromFile.SeriesPositionRaw);
            }

            return metadata;
        }

        /// <summary>
        /// The same, taking the file's contribution as the raw tag dictionary a container
        /// read produces rather than a probed <see cref="AudioMetadata"/>.
        /// </summary>
        public static AudioMetadata Create(
            Audiobook audiobook,
            IReadOnlyDictionary<string, string>? fileTags) =>
            Create(audiobook, fileTags == null ? null : FromTags(fileTags));

        /// <summary>
        /// Read the handful of fields a fallback needs out of a file's tags, using the
        /// same keys the catalog writes so a value Listenarr wrote last time is the value
        /// it reads back this time.
        /// </summary>
        private static AudioMetadata FromTags(IReadOnlyDictionary<string, string> tags)
        {
            string? Get(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            var seriesPosition = Get(TagCatalog.SeriesPosition, "SERIES-PART", "MOVEMENT");

            // A date tag may be a bare year or a full ISO date; only the year is a year.
            var rawDate = Get(TagCatalog.Date);
            var yearText = rawDate == null ? null : rawDate[..Math.Min(4, rawDate.Length)];

            return new AudioMetadata
            {
                Artist = Get(TagCatalog.Artist) ?? string.Empty,
                AlbumArtist = Get(TagCatalog.AlbumArtist) ?? string.Empty,
                Genre = Get(TagCatalog.Genre) ?? string.Empty,
                // "comment" carries the blurb in most taggers and is far more common in
                // the wild than "description", even though only the latter reaches Plex.
                Description = Get(TagCatalog.Description, TagCatalog.Comment),
                Subtitle = Get(TagCatalog.Subtitle, "subtitle", "TIT3"),
                Narrator = Get(TagCatalog.Composer, "narrator"),
                Publisher = Get(TagCatalog.Publisher),
                Language = Get(TagCatalog.Language),
                Asin = Get(TagCatalog.Asin, "AUDIBLE_ASIN"),
                Series = Get(TagCatalog.Series, "show"),
                SeriesPositionRaw = seriesPosition,
                SeriesPosition = decimal.TryParse(
                    seriesPosition,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                    ? parsed
                    : null,
                Year = int.TryParse(
                    yearText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var year) && year > 0
                    ? year
                    : null
            };
        }

        private static string? FirstNonEmpty(params string?[] candidates) =>
            candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
    }
}
