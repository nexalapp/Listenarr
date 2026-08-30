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

namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>
    /// Reads what an SMF login form needs posting back.
    ///
    /// SMF protects the login with a session token whose field name is randomised per
    /// installation, so it cannot be hardcoded — every hidden input is carried across
    /// instead. That also keeps this working if the forum adds another hidden field.
    /// </summary>
    public static partial class SmfLoginForm
    {
        [GeneratedRegex(@"<input[^>]*type=[""']hidden[""'][^>]*>", RegexOptions.IgnoreCase)]
        private static partial Regex HiddenInput();

        [GeneratedRegex(@"\bname=[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
        private static partial Regex NameAttribute();

        [GeneratedRegex(@"\bvalue=[""']([^""']*)[""']", RegexOptions.IgnoreCase)]
        private static partial Regex ValueAttribute();

        /// <summary>
        /// Every hidden field on the login page, which includes SMF's session token.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ReadHiddenFields(string? html)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(html))
            {
                return fields;
            }

            foreach (Match input in HiddenInput().Matches(html))
            {
                var name = NameAttribute().Match(input.Value);
                if (!name.Success)
                {
                    continue;
                }

                var value = ValueAttribute().Match(input.Value);
                fields[WebUtility.HtmlDecode(name.Groups[1].Value)] =
                    value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : string.Empty;
            }

            return fields;
        }

        /// <summary>
        /// Whether a page shows a signed-in member. SMF answers 200 for a logged-out page,
        /// so the status code says nothing; the logout link is only rendered when signed in.
        /// </summary>
        public static bool IsSignedIn(string? html) =>
            html is { Length: > 0 } && html.Contains("action=logout", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the forum rejected the credentials, as opposed to failing some other
        /// way. Worth distinguishing: a wrong password needs the operator, a timeout does
        /// not.
        /// </summary>
        public static bool LooksLikeBadCredentials(string? html) =>
            html is { Length: > 0 }
            && (html.Contains("password was incorrect", StringComparison.OrdinalIgnoreCase)
                || html.Contains("incorrect password", StringComparison.OrdinalIgnoreCase)
                || html.Contains("that username does not exist", StringComparison.OrdinalIgnoreCase)
                || html.Contains("username or password was incorrect", StringComparison.OrdinalIgnoreCase));
    }
}
