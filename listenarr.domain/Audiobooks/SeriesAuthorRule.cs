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
namespace Listenarr.Domain.Audiobooks
{
    /// <summary>An operator's answer for one series, overriding the rule.</summary>
    public sealed record SeriesAuthorOverride(
        [property: System.Text.Json.Serialization.JsonPropertyName("series")] string Series,
        [property: System.Text.Json.Serialization.JsonPropertyName("author")] string Author);

    /// <summary>One book's contribution to deciding who a series is filed under.</summary>
    public sealed record SeriesAuthorCandidate(
        string Series,
        string? SeriesNumber,
        int? Year,
        string? FirstAuthor);

    /// <summary>
    /// Which author a series is filed under: the author of its first book.
    ///
    /// A series that changes hands - Dune, most long-running franchises - would otherwise
    /// scatter across author folders and author pages, and a reader looking for the next
    /// book has to know who wrote it before they can find it. The originator created the
    /// idea and is the reason the series exists; every book files under them, and the
    /// book's own credit still goes wherever a full author list is written.
    ///
    /// "First" is the book at the lowest series position in the library; a tie falls back
    /// to the earliest year. Position rather than year because the year a record carries
    /// is usually the audio edition's, not the first printing's - by year, Harry Potter
    /// files under the playwright of the 2001-recorded Cursed Child. Books with no
    /// position sort last. An operator-set override wins over all of it, and an override
    /// with no author exempts the series: an anthology has no originator, and filing the
    /// Forward Collection under whichever contributor sorted first hides every other
    /// author's book under the wrong name.
    /// </summary>
    public static class SeriesAuthorRule
    {
        public static string Key(string? series) => (series ?? string.Empty).Trim().ToLowerInvariant();

        public static IReadOnlyDictionary<string, string> Compute(
            IEnumerable<SeriesAuthorCandidate> candidates,
            IEnumerable<SeriesAuthorOverride>? overrides = null)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Series)
                    && !string.IsNullOrWhiteSpace(candidate.FirstAuthor))
                .GroupBy(candidate => Key(candidate.Series)))
            {
                var first = group
                    .OrderBy(candidate => Position(candidate.SeriesNumber))
                    .ThenBy(candidate => candidate.Year ?? int.MaxValue)
                    .First();
                result[group.Key] = first.FirstAuthor!.Trim();
            }

            foreach (var item in overrides ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Series))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Author))
                {
                    // Exempt: no entry means {Author} falls back to each book's own credit.
                    result.Remove(Key(item.Series));
                }
                else
                {
                    result[Key(item.Series)] = item.Author.Trim();
                }
            }

            return result;
        }

        private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

        public static string EmptyOverridesJson => "[]";

        public static IReadOnlyList<SeriesAuthorOverride> ParseOverrides(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<SeriesAuthorOverride>>(json, Json) ?? [];
            }
            catch (System.Text.Json.JsonException)
            {
                return [];
            }
        }

        private static double Position(string? number)
        {
            if (string.IsNullOrWhiteSpace(number))
            {
                return double.MaxValue;
            }

            // "1-3", "2.5", "07": the leading number is the position; anything else sorts last.
            var digits = new string(number.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            return double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : double.MaxValue;
        }
    }
}
