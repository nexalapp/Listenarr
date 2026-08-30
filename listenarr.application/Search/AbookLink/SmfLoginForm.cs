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

        [GeneratedRegex(@"<form[^>]*\baction=[""']([^""']*action=login2[^""']*)[""']", RegexOptions.IgnoreCase)]
        private static partial Regex LoginAction();

        [GeneratedRegex(@"hashLoginPassword\s*\(\s*this\s*,\s*['""]([0-9a-f]{8,64})['""]", RegexOptions.IgnoreCase)]
        private static partial Regex HashSalt();

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
        /// The URL the login form actually posts to.
        ///
        /// SMF puts the PHP session id in the query string as well as in a cookie, so the
        /// form's own action has to be used rather than a constructed one — posting to a
        /// bare action=login2 loses the session the login page just started.
        /// </summary>
        public static string? ReadLoginAction(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var match = LoginAction().Match(html);
            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
        }

        /// <summary>
        /// The salt SMF hashes the password with in the browser.
        ///
        /// The login form carries
        /// <c>onsubmit="hashLoginPassword(this, '&lt;salt&gt;')"</c>, and forums that do not
        /// accept the plaintext fallback expect the hashed field instead. Read from the
        /// attribute rather than inferred from the hidden fields, because that is the
        /// value the browser would actually use.
        /// </summary>
        public static string? ReadPasswordHashSalt(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var match = HashSalt().Match(html);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// The strict signal: this page positively shows a signed-in member.
        ///
        /// <see cref="IsSignedIn"/> is deliberately lenient because the site's tools
        /// render no navigation, but that leniency must not be used to decide whether a
        /// login worked — an interstitial with neither marker would read as success.
        /// Verification fetches the forum index, which always renders navigation, so it
        /// can demand the positive signal.
        /// </summary>
        public static bool IsDefinitelySignedIn(string? html) =>
            html is { Length: > 0 } && html.Contains("action=logout", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Reproduces SMF's client-side password hash:
        /// <c>sha1(sha1(lowercase(username) + password) + salt)</c>.
        /// </summary>
        public static string HashPassword(string username, string password, string salt)
        {
            ArgumentNullException.ThrowIfNull(username);
            ArgumentNullException.ThrowIfNull(password);
            ArgumentNullException.ThrowIfNull(salt);

            var inner = Sha1(username.ToLowerInvariant() + password);
            return Sha1(inner + salt);
        }

        private static string Sha1(string value)
        {
            var digest = System.Security.Cryptography.SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(digest).ToLowerInvariant();
        }

        /// <summary>
        /// Whether a page shows a signed-in member. SMF answers 200 for a logged-out page,
        /// so the status code says nothing.
        ///
        /// Two signals, because one is not enough. Forum pages render a logout link when
        /// signed in, but the site's own tools — the fuzzy search among them — render no
        /// navigation at all, so requiring a logout link reports a perfectly good session
        /// as expired. Every signed-out page carries a login form instead, and its absence
        /// is the signal that generalises.
        /// </summary>
        public static bool IsSignedIn(string? html)
        {
            if (html is not { Length: > 0 })
            {
                return false;
            }

            if (html.Contains("action=logout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !html.Contains("action=login2", StringComparison.OrdinalIgnoreCase);
        }

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
