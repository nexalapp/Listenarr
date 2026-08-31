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
using System.Text.Json.Nodes;

namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>
    /// Encrypts the abook.link password inside an indexer's AdditionalSettings before it
    /// is stored.
    ///
    /// The settings blob is otherwise saved verbatim, which would leave a forum password
    /// readable to anyone with the database file. Encrypting on the way in means the value
    /// at rest is ciphertext, and the redactor already keeps it out of API responses.
    /// </summary>
    public static class AbookSecretProtection
    {
        /// <summary>
        /// Returns the settings with the password encrypted. A value already encrypted is
        /// left alone, so saving a second time does not double-encrypt it.
        /// </summary>
        public static string? Protect(
            string? additionalSettings,
            Func<string, string> protect,
            Func<string, string> unprotect)
        {
            ArgumentNullException.ThrowIfNull(protect);
            ArgumentNullException.ThrowIfNull(unprotect);

            if (string.IsNullOrWhiteSpace(additionalSettings))
            {
                return additionalSettings;
            }

            JsonObject? root;
            try
            {
                root = JsonNode.Parse(additionalSettings) as JsonObject;
            }
            catch (JsonException)
            {
                return additionalSettings;
            }

            if (root is null
                || root[AbookLinkSettings.PasswordProperty]?.GetValue<string>() is not { Length: > 0 } password)
            {
                return additionalSettings;
            }

            if (IsAlreadyProtected(password, unprotect))
            {
                return additionalSettings;
            }

            root[AbookLinkSettings.PasswordProperty] = protect(password);
            return root.ToJsonString();
        }

        private static bool IsAlreadyProtected(string value, Func<string, string> unprotect)
        {
            try
            {
                unprotect(value);
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }
    }
}
