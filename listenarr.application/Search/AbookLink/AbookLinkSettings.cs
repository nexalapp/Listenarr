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
    /// <summary>
    /// Reads abook.link configuration from an indexer's AdditionalSettings JSON.
    /// </summary>
    public static class AbookLinkSettings
    {
        /// <summary>
        /// Property holding the forum session cookie.
        ///
        /// A session value rather than a password, following the MyAnonamouse precedent —
        /// Listenarr never needs to hold forum credentials. The name contains "cookie" so
        /// ApiResponseRedactor.IsSensitiveKey redacts it from API responses automatically.
        /// </summary>
        public const string SessionCookieProperty = "abook_session_cookie";

        public static string? TryGetSessionCookie(string? additionalSettings)
        {
            if (string.IsNullOrWhiteSpace(additionalSettings))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(additionalSettings);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty(SessionCookieProperty, out var value)
                    || value.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var cookie = value.GetString();
                return string.IsNullOrWhiteSpace(cookie) ? null : cookie.Trim();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
