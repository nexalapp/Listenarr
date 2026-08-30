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

namespace Listenarr.Infrastructure.Search.AbookLink
{
    /// <summary>
    /// Turns a topic page into the plain text the post parser expects.
    ///
    /// The NFO in an abook.link post is laid out as text — <c>Media Format: MP3</c> on its
    /// own line — but arrives wrapped in markup. Handing the raw HTML to the parser finds
    /// none of it, and worse, reads the page's inline JavaScript as labelled fields, which
    /// is why a first live run reported variables like <c>sScriptUrl</c> as unrecognised
    /// NFO labels.
    /// </summary>
    public static partial class AbookPostHtml
    {
        [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex ScriptOrStyle();

        [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
        private static partial Regex LineBreak();

        [GeneratedRegex(@"</(p|div|tr|li|h[1-6]|blockquote)\s*>", RegexOptions.IgnoreCase)]
        private static partial Regex BlockEnd();

        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex AnyTag();

        [GeneratedRegex(@"[ \t]+")]
        private static partial Regex Spaces();

        [GeneratedRegex(@"\n{3,}")]
        private static partial Regex BlankRuns();

        /// <summary>
        /// Extracts readable text, preserving the line structure the NFO depends on.
        /// </summary>
        public static string ToText(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            // Scripts first: their contents are not page text, and their variable
            // assignments look exactly like labelled NFO fields once tags are stripped.
            var text = ScriptOrStyle().Replace(html, "\n");

            text = LineBreak().Replace(text, "\n");
            text = BlockEnd().Replace(text, "\n");
            text = AnyTag().Replace(text, string.Empty);
            text = WebUtility.HtmlDecode(text);

            // Normalise horizontal space but keep newlines: the parser reads line by line.
            text = Spaces().Replace(text, " ");
            text = string.Join('\n', text.Split('\n').Select(line => line.Trim()));
            text = BlankRuns().Replace(text, "\n\n");

            return text.Trim();
        }
    }
}
