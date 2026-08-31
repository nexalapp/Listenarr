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
using System.Net;
using System.Text.RegularExpressions;

namespace Listenarr.Domain.Audiobooks.Tagging
{
    /// <summary>
    /// Turning a metadata provider's blurb into text fit for a tag.
    ///
    /// <para>
    /// Audible and Audnexus return a description as HTML — <c>&lt;p&gt;</c> around each
    /// paragraph, <c>&lt;b&gt;</c> and <c>&lt;i&gt;</c> inside them. Nothing that reads
    /// the MP4 <c>desc</c> atom renders markup, so writing it verbatim puts literal
    /// angle brackets into the summary Plex shows.
    /// </para>
    /// <para>
    /// Not the HtmlAgilityPack extractor this repository already has, for two reasons:
    /// the domain cannot reference infrastructure, and that extractor returns
    /// <c>InnerText</c>, which runs "&lt;p&gt;One&lt;/p&gt;&lt;p&gt;Two&lt;/p&gt;"
    /// together as "OneTwo". A blurb's paragraph breaks are the part worth keeping.
    /// The output is plain text rather than markup, so an imperfect strip leaves stray
    /// characters, never anything that is later interpreted.
    /// </para>
    /// </summary>
    public static partial class TagText
    {
        /// <summary>Block-level tags that end a line rather than merely disappearing.</summary>
        [GeneratedRegex(
            @"</?\s*(?:br|p|div|li|ul|ol|h[1-6]|blockquote)\s*/?\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex BlockTagRegex();

        [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant)]
        private static partial Regex AnyTagRegex();

        /// <summary>Three or more newlines is never a deliberate gap; two is a paragraph break.</summary>
        [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
        private static partial Regex ExcessBlankLinesRegex();

        /// <summary>Horizontal whitespace only, so paragraph breaks survive the collapse.</summary>
        [GeneratedRegex(@"[^\S\r\n]{2,}", RegexOptions.CultureInvariant)]
        private static partial Regex RepeatedSpacesRegex();

        [GeneratedRegex(@"[^\S\r\n]*\n[^\S\r\n]*", RegexOptions.CultureInvariant)]
        private static partial Regex PaddedNewlineRegex();

        public static string FromHtml(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // Nothing that looks like markup: leave it exactly as it is rather than
            // running a description that merely mentions "a < b" through a stripper.
            if (value.IndexOf('<') < 0 && value.IndexOf('&') < 0)
            {
                return value.Trim();
            }

            var text = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            text = BlockTagRegex().Replace(text, "\n");
            text = AnyTagRegex().Replace(text, string.Empty);

            // After the tags are gone: "&amp;" is an ampersand, and a provider that
            // double-encodes would otherwise leave one in the tag.
            text = WebUtility.HtmlDecode(text);

            text = PaddedNewlineRegex().Replace(text, "\n");
            text = ExcessBlankLinesRegex().Replace(text, "\n\n");
            text = RepeatedSpacesRegex().Replace(text, " ");

            return text.Trim();
        }
    }
}
