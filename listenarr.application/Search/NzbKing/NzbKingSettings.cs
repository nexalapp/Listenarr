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

namespace Listenarr.Application.Search.NzbKing
{
    /// <summary>
    /// Reads NZBKing configuration out of an indexer's AdditionalSettings JSON.
    /// </summary>
    public static class NzbKingSettings
    {
        /// <summary>
        /// Property holding a single API key, as installs before the key pool stored it.
        /// Still read, because an install that has not been re-saved has nothing else.
        ///
        /// The name contains "api_key" deliberately: ApiResponseRedactor.IsSensitiveKey
        /// matches on that substring, so the value is redacted from API responses and
        /// re-merged on save without any redactor change.
        /// </summary>
        public const string ApiKeyProperty = "nzbking_api_key";

        /// <summary>
        /// Property holding every API key, as an array of strings.
        ///
        /// NZBKing issues one key per CAPTCHA solved by a person, and each key carries its
        /// own 100-token allowance, so several keys are several allowances rather than a
        /// larger one. The name contains "api_key" for the same redaction reason as
        /// <see cref="ApiKeyProperty"/>; array values are redacted element by element.
        /// </summary>
        public const string ApiKeysProperty = "nzbking_api_keys";

        /// <summary>
        /// Rewrites a settings blob so every key lives in <see cref="ApiKeysProperty"/>,
        /// with <see cref="ApiKeyProperty"/> folded in and removed.
        ///
        /// Applied on the way out to a caller and again to the stored blob when a save is
        /// merged, so both sides agree on the shape. They have to: the edit form sends back
        /// placeholders that are resolved against what is stored, and a form shown an array
        /// while the database still held a single property would have its placeholder match
        /// nothing and quietly lose the key.
        ///
        /// A blob with nothing to migrate is returned exactly as it arrived rather than
        /// re-serialised, so this cannot churn settings it has no business touching.
        /// </summary>
        public static string? Normalise(string? additionalSettings)
        {
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

            if (root is null || !root.ContainsKey(ApiKeyProperty))
            {
                return additionalSettings;
            }

            // Read through TryGetValue rather than GetValue: the blob is whatever was
            // stored, and a malformed value must leave the settings alone instead of
            // throwing from the middle of a save.
            var legacy = AsString(root[ApiKeyProperty])?.Trim();
            root.Remove(ApiKeyProperty);

            if (!string.IsNullOrEmpty(legacy))
            {
                if (root[ApiKeysProperty] is not JsonArray keys)
                {
                    keys = new JsonArray();
                    root[ApiKeysProperty] = keys;
                }

                var present = keys.Any(element =>
                    string.Equals(AsString(element)?.Trim(), legacy, StringComparison.Ordinal));

                if (!present)
                {
                    keys.Add(legacy);
                }
            }

            return root.ToJsonString();
        }

        private static string? AsString(JsonNode? node)
            => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        /// <summary>
        /// Extracts the first configured API key, or null when the indexer has none.
        ///
        /// Only for callers that genuinely want one key and do not care which. Anything
        /// that meters, reports on, or keeps a key alive wants <see cref="TryGetApiKeys"/>:
        /// reading one key out of a pool silently spends, shows and protects a fraction of
        /// what is configured.
        /// </summary>
        public static string? TryGetApiKey(string? additionalSettings)
            => TryGetApiKeys(additionalSettings).FirstOrDefault();

        /// <summary>
        /// Extracts every configured API key, in no significant order.
        ///
        /// Order here does not decide which key is spent - the budget picks that from the
        /// balances - so this only has to be stable and free of duplicates. A duplicate
        /// would otherwise be offered to the budget twice and look like two allowances
        /// when it is one.
        /// </summary>
        public static IReadOnlyList<string> TryGetApiKeys(string? additionalSettings)
        {
            if (string.IsNullOrWhiteSpace(additionalSettings))
            {
                return [];
            }

            try
            {
                using var document = JsonDocument.Parse(additionalSettings);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return [];
                }

                var keys = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                void Add(string? candidate)
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        return;
                    }

                    var key = candidate.Trim();
                    if (seen.Add(key))
                    {
                        keys.Add(key);
                    }
                }

                if (document.RootElement.TryGetProperty(ApiKeysProperty, out var array)
                    && array.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in array.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.String)
                        {
                            Add(element.GetString());
                        }
                    }
                }

                // The legacy single property is read after the array, and both are read on
                // every load: an install upgraded in place holds only the single property
                // until the source is next saved, and losing its key would spend a fresh
                // allowance it does not have.
                if (document.RootElement.TryGetProperty(ApiKeyProperty, out var single)
                    && single.ValueKind == JsonValueKind.String)
                {
                    Add(single.GetString());
                }

                return keys;
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}
