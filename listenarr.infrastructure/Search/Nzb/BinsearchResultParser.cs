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
using System.Net;
using System.Text.RegularExpressions;
using Listenarr.Application.Search.Nzb;

namespace Listenarr.Infrastructure.Search.Nzb
{
    /// <summary>
    /// Reads Binsearch's result rows.
    ///
    /// Binsearch is an HTML frontend over the same index NZBIndex serves as JSON — a
    /// row's checkbox name is the base64 encoding of the NZBIndex id for the same article.
    /// It is kept because its <c>/nzb</c> endpoint assembles one NZB from several selected
    /// parts, which is the only verified way to fetch a release whose poster says it does
    /// not appear as a single collection.
    /// </summary>
    public static partial class BinsearchResultParser
    {
        [GeneratedRegex(@"<tr\b[^>]*>.*?</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex Row();

        [GeneratedRegex(@"<input[^>]*type=""checkbox""[^>]*name=""([A-Za-z0-9+/=_-]+)""", RegexOptions.IgnoreCase)]
        private static partial Regex CheckboxId();

        [GeneratedRegex(@"href=""/details/([A-Za-z0-9+/=_-]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex DetailsLink();

        [GeneratedRegex(@">\s*([\d.,]+)\s*(TB|GB|MB|KB|B)\s*<", RegexOptions.IgnoreCase)]
        private static partial Regex Size();

        [GeneratedRegex(@"<span([^>]*)>\s*(\d+)\s+(?:complete\s+)?Files?\s*</span>", RegexOptions.IgnoreCase)]
        private static partial Regex FileCount();

        [GeneratedRegex(@"href=""/search\?poster=([^""]+)""", RegexOptions.IgnoreCase)]
        private static partial Regex Poster();

        [GeneratedRegex(@"href=""/\?g=([^""]+)""", RegexOptions.IgnoreCase)]
        private static partial Regex Group();

        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex Tag();

        public static IReadOnlyList<NzbCandidate> Parse(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            var results = new List<NzbCandidate>();

            foreach (Match row in Row().Matches(html))
            {
                var candidate = ReadRow(row.Value);
                if (candidate != null)
                {
                    results.Add(candidate);
                }
            }

            return results;
        }

        private static NzbCandidate? ReadRow(string row)
        {
            var details = DetailsLink().Match(row);
            if (!details.Success)
            {
                return null;
            }

            // The checkbox name is the canonical id; the details href repeats it, so either
            // works and the second is a fallback if the markup drops the checkbox.
            var checkbox = CheckboxId().Match(row);
            var id = checkbox.Success ? checkbox.Groups[1].Value : details.Groups[1].Value;

            var subject = WebUtility.HtmlDecode(Tag().Replace(details.Groups[2].Value, string.Empty)).Trim();
            if (subject.Length == 0)
            {
                return null;
            }

            int? files = null;
            bool? complete = null;
            var count = FileCount().Match(row);
            if (count.Success)
            {
                if (int.TryParse(count.Groups[2].Value, out var parsed))
                {
                    files = parsed;
                }

                // Binsearch marks a partial collection with an "incomplete" class rather
                // than saying so in words.
                complete = !count.Groups[1].Value.Contains("incomplete", StringComparison.OrdinalIgnoreCase);
            }

            return new NzbCandidate(
                id,
                subject,
                ReadSize(row),
                files,
                complete,
                Group().Match(row) is { Success: true } g ? [WebUtility.UrlDecode(g.Groups[1].Value)] : null,
                Poster().Match(row) is { Success: true } p ? WebUtility.UrlDecode(p.Groups[1].Value) : null);
        }

        private static long? ReadSize(string row)
        {
            var match = Size().Match(row);
            if (!match.Success
                || !double.TryParse(match.Groups[1].Value.Replace(",", string.Empty),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            {
                return null;
            }

            var multiplier = match.Groups[2].Value.ToUpperInvariant() switch
            {
                "TB" => 1024L * 1024 * 1024 * 1024,
                "GB" => 1024L * 1024 * 1024,
                "MB" => 1024L * 1024,
                "KB" => 1024L,
                _ => 1L
            };

            return (long)(amount * multiplier);
        }
    }
}
