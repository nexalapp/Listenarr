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

namespace Listenarr.Application.Search.NzbKing
{
    /// <summary>
    /// Reads NZBKing configuration out of an indexer's AdditionalSettings JSON.
    /// </summary>
    public static class NzbKingSettings
    {
        /// <summary>
        /// Property holding the personal API key NZBKing issues after its CAPTCHA.
        ///
        /// The name contains "api_key" deliberately: ApiResponseRedactor.IsSensitiveKey
        /// matches on that substring, so the value is redacted from API responses and
        /// re-merged on save without any redactor change.
        /// </summary>
        public const string ApiKeyProperty = "nzbking_api_key";

        /// <summary>
        /// Extracts the API key, or null when the indexer has none configured.
        /// </summary>
        public static string? TryGetApiKey(string? additionalSettings)
        {
            if (string.IsNullOrWhiteSpace(additionalSettings))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(additionalSettings);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (!document.RootElement.TryGetProperty(ApiKeyProperty, out var value)
                    || value.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var key = value.GetString();
                return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
