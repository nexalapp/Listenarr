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
using System.Text.RegularExpressions;

namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>
    /// What a topic title alone tells us.
    /// </summary>
    public readonly record struct AbookTitleParts(
        string? Author,
        string? Title,
        string? SeriesName,
        string? SeriesPosition,
        int? Year,
        string? Narrator,
        bool IsNotARelease,
        bool IsArchiveSpot);

    /// <summary>
    /// Parses an abook.link topic title.
    ///
    /// Titles hold to a far tighter shape than post bodies —
    /// <c>Author - Series NN - Title (Year)</c> — which makes them the dependable fallback
    /// when a body omits its series fields or has no NFO at all. Two wrinkles: narrator
    /// shorthand appears in braces (<c>{Price}</c>), and archive imports arrived with their
    /// dashes mangled into question marks by an old encoding.
    /// </summary>
    public static partial class AbookTopicTitle
    {
        [GeneratedRegex(@"^\s*\[(SPOT|REQUEST|FILLED|Reading Order|Spotted)\]", RegexOptions.IgnoreCase)]
        private static partial Regex Prefix();

        [GeneratedRegex(@"\((\d{4})(?:/\d{4})?\)")]
        private static partial Regex Year();

        [GeneratedRegex(@"\{([^}]{1,40})\}")]
        private static partial Regex NarratorBraces();

        [GeneratedRegex(@"^(.*?)[\s,]*(\d{1,3}(?:\.\d)?)\s*$")]
        private static partial Regex TrailingNumber();

        public static AbookTitleParts Parse(string? topicTitle)
        {
            if (string.IsNullOrWhiteSpace(topicTitle))
            {
                return default;
            }

            var working = topicTitle.Trim();
            var isArchive = false;
            var notARelease = false;

            var prefix = Prefix().Match(working);
            while (prefix.Success)
            {
                var kind = prefix.Groups[1].Value.ToLowerInvariant();
                if (kind is "spot" or "spotted") isArchive = true;
                else notARelease = true;

                working = working[prefix.Length..].Trim();
                prefix = Prefix().Match(working);
            }

            string? narrator = null;
            var braces = NarratorBraces().Match(working);
            if (braces.Success)
            {
                narrator = braces.Groups[1].Value.Trim();
                working = working.Remove(braces.Index, braces.Length).Trim();
            }

            int? year = null;
            var yearMatch = Year().Match(working);
            if (yearMatch.Success)
            {
                year = int.Parse(yearMatch.Groups[1].Value);
                working = working.Remove(yearMatch.Index, yearMatch.Length).Trim();
            }

            // Archive titles had their dashes replaced by "?" in an old encoding, so treat
            // that as a separator too rather than losing the split entirely.
            var separator = working.Contains(" - ", StringComparison.Ordinal) ? " - " : " ? ";
            var segments = working.Split(separator, StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .ToArray();

            string? author = null, title = null, series = null, position = null;

            if (segments.Length >= 3)
            {
                author = segments[0];
                title = segments[^1];
                var (name, pos) = SplitTrailingNumber(string.Join(separator, segments[1..^1]));
                series = name;
                position = pos;
            }
            else if (segments.Length == 2)
            {
                author = segments[0];
                title = segments[1];
            }
            else if (segments.Length == 1)
            {
                title = segments[0];
            }

            return new AbookTitleParts(author, title, series, position, year, narrator, notARelease, isArchive);
        }

        private static (string? Name, string? Position) SplitTrailingNumber(string value)
        {
            var match = TrailingNumber().Match(value.Trim());
            if (!match.Success)
            {
                return (value.Trim(), null);
            }

            var name = match.Groups[1].Value.Trim();
            return name.Length == 0 ? (value.Trim(), null) : (name, match.Groups[2].Value);
        }
    }
}
