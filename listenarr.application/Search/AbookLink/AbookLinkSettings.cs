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
using System.Text.Json;

namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>What an abook.link source is configured with.</summary>
    /// <param name="Username">Forum account name.</param>
    /// <param name="Password">Forum password, decrypted and ready to use.</param>
    /// <param name="SessionCookie">
    /// A cookie supplied directly, for anyone who would rather not store a password.
    /// Used in preference to logging in when present.
    /// </param>
    public sealed record AbookCredentials(string? Username, string? Password, string? SessionCookie)
    {
        public bool CanSignIn => Username is { Length: > 0 } && Password is { Length: > 0 };

        public bool HasAnything => CanSignIn || SessionCookie is { Length: > 0 };
    }

    /// <summary>
    /// Reads abook.link configuration from an indexer's AdditionalSettings JSON.
    ///
    /// Two ways in: a username and password, which Listenarr uses to sign in and keep a
    /// session; or a session cookie pasted directly, for anyone who would rather the
    /// application never held their password. The cookie wins when both are present.
    ///
    /// Both secret property names contain a substring
    /// <c>ApiResponseRedactor.IsSensitiveKey</c> matches, so neither is ever echoed back
    /// over the API, and its merge step restores the stored value when a redacted
    /// placeholder is saved.
    /// </summary>
    public static class AbookLinkSettings
    {
        public const string UsernameProperty = "abook_username";
        public const string PasswordProperty = "abook_password";
        public const string SessionCookieProperty = "abook_session_cookie";

        /// <summary>
        /// Reads the credentials. <paramref name="unprotect"/> decrypts the stored
        /// password; a value that fails to decrypt is treated as absent rather than passed
        /// through, since sending ciphertext as a password would just fail confusingly.
        /// </summary>
        public static AbookCredentials Read(string? additionalSettings, Func<string, string>? unprotect = null)
        {
            var username = ReadProperty(additionalSettings, UsernameProperty);
            var cookie = ReadProperty(additionalSettings, SessionCookieProperty);
            var stored = ReadProperty(additionalSettings, PasswordProperty);

            string? password = null;
            if (stored is { Length: > 0 })
            {
                if (unprotect is null)
                {
                    password = stored;
                }
                else
                {
                    try
                    {
                        password = unprotect(stored);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        password = null;
                    }
                }
            }

            return new AbookCredentials(username, password, cookie);
        }

        public static string? TryGetSessionCookie(string? additionalSettings) =>
            ReadProperty(additionalSettings, SessionCookieProperty);

        private static string? ReadProperty(string? additionalSettings, string property)
        {
            if (string.IsNullOrWhiteSpace(additionalSettings))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(additionalSettings);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty(property, out var value)
                    || value.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var text = value.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
