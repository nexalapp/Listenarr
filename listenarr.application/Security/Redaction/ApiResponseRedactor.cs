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
using Listenarr.Application.Search.NzbKing;

namespace Listenarr.Application.Security.Redaction;

public static class ApiResponseRedactor
{
    public const string RedactedValue = "REDACTED";

    public static ApiConfiguration RedactApiConfiguration(ApiConfiguration config)
    {
        var clone = Clone(config);
        if (!string.IsNullOrWhiteSpace(clone.ApiKey))
        {
            clone.ApiKey = RedactedValue;
        }

        clone.Headers = RedactStringDictionary(clone.Headers);
        clone.Parameters = RedactStringDictionary(clone.Parameters);
        return clone;
    }

    public static DownloadClientConfiguration RedactDownloadClientConfiguration(DownloadClientConfiguration config)
    {
        var clone = Clone(config);

        if (!string.IsNullOrWhiteSpace(clone.Username))
        {
            clone.Username = RedactedValue;
        }

        if (!string.IsNullOrWhiteSpace(clone.Password))
        {
            clone.Password = RedactedValue;
        }

        clone.Settings = RedactObjectDictionary(clone.Settings);
        return clone;
    }

    public static ApplicationSettings RedactApplicationSettings(ApplicationSettings settings)
    {
        var clone = Clone(settings);
        clone.AdminUsername = null;
        clone.AdminPassword = null;

        if (!string.IsNullOrWhiteSpace(clone.WebhookUrl))
        {
            clone.WebhookUrl = RedactedValue;
        }

        if (!string.IsNullOrWhiteSpace(clone.DiscordBotToken))
        {
            clone.DiscordBotToken = RedactedValue;
        }

        if (!string.IsNullOrWhiteSpace(clone.ProwlarrApiKeyEncrypted))
        {
            clone.ProwlarrApiKeyEncrypted = RedactedValue;
        }

        if (clone.Webhooks != null)
        {
            foreach (var webhook in clone.Webhooks.Where(w => !string.IsNullOrWhiteSpace(w.Url)))
            {
                webhook.Url = RedactedValue;
            }
        }

        return clone;
    }

    public static StartupConfig RedactStartupConfig(StartupConfig config, bool redactApiKey = true)
    {
        var clone = Clone(config);
        if (redactApiKey && !string.IsNullOrWhiteSpace(clone.ApiKey))
        {
            clone.ApiKey = RedactedValue;
        }

        if (!string.IsNullOrWhiteSpace(clone.SslCertPassword))
        {
            clone.SslCertPassword = RedactedValue;
        }

        return clone;
    }

    public static Indexer RedactIndexer(Indexer indexer)
    {
        var clone = Clone(indexer);
        if (!string.IsNullOrWhiteSpace(clone.ApiKey))
        {
            clone.ApiKey = RedactedValue;
        }

        if (!string.IsNullOrWhiteSpace(clone.AdditionalSettings))
        {
            // Migrated before it is redacted, so a caller is shown the shape a save is
            // merged against. Showing the old shape and merging against the new one is how
            // a placeholder comes back matching nothing and takes the stored secret with
            // it. The clone is a copy, so nothing here reaches the stored entity.
            clone.AdditionalSettings = RedactJsonSettings(
                NzbKingSettings.Normalise(clone.AdditionalSettings) ?? clone.AdditionalSettings);
        }

        return clone;
    }

    /// <summary>
    /// Merge incoming AdditionalSettings JSON with existing DB values, preserving
    /// real secrets when the incoming payload contains "REDACTED" placeholders.
    /// </summary>
    public static string? MergeAdditionalSettings(string? existingJson, string? incomingJson)
    {
        // Fully redacted or empty → preserve existing
        if (string.IsNullOrWhiteSpace(incomingJson) || incomingJson == RedactedValue)
            return existingJson;

        if (string.IsNullOrWhiteSpace(existingJson))
            return incomingJson;

        try
        {
            using var incomingDoc = JsonDocument.Parse(incomingJson);
            if (incomingDoc.RootElement.ValueKind != JsonValueKind.Object)
                return incomingJson;

            // Quick scan: any values are "REDACTED", or an array holding element
            // placeholders?
            bool hasRedacted = incomingDoc.RootElement.EnumerateObject().Any(prop =>
                (prop.Value.ValueKind == JsonValueKind.String &&
                 prop.Value.GetString() == RedactedValue) ||
                (IsSensitiveKey(prop.Name) &&
                 prop.Value.ValueKind == JsonValueKind.Array &&
                 prop.Value.EnumerateArray().Any(element =>
                     element.ValueKind == JsonValueKind.String &&
                     TryReadRedactedElement(element.GetString(), out _))));

            if (!hasRedacted)
                return incomingJson;

            // Merge: use incoming values except where they're "REDACTED" — keep existing for those
            using var existingDoc = JsonDocument.Parse(existingJson);
            if (existingDoc.RootElement.ValueKind != JsonValueKind.Object)
                return incomingJson;

            using var ms = new System.IO.MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in incomingDoc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String &&
                        prop.Value.GetString() == RedactedValue &&
                        existingDoc.RootElement.TryGetProperty(prop.Name, out var existingValue))
                    {
                        writer.WritePropertyName(prop.Name);
                        existingValue.WriteTo(writer);
                    }
                    else if (IsSensitiveKey(prop.Name) && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        existingDoc.RootElement.TryGetProperty(prop.Name, out var existingArray);
                        writer.WritePropertyName(prop.Name);
                        MergeSecretArray(writer, prop.Value, existingArray);
                    }
                    else
                    {
                        writer.WritePropertyName(prop.Name);
                        prop.Value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (JsonException)
        {
            return incomingJson;
        }
    }

    /// <summary>
    /// Placeholder standing in for the secret at <paramref name="index"/> of a redacted
    /// array. It names the position because a list can be shortened or reordered between
    /// being read and being sent back: merging a bare "REDACTED" by position would restore
    /// the wrong entry when one is removed, and silently resurrect a deleted secret.
    /// </summary>
    private static string RedactedElement(int index) => $"{RedactedValue}#{index}";

    private static bool TryReadRedactedElement(string? value, out int index)
    {
        index = -1;
        if (value is null || !value.StartsWith(RedactedValue + "#", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(
            value.AsSpan(RedactedValue.Length + 1),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out index) && index >= 0;
    }

    /// <summary>
    /// Writes an incoming array of secrets, resolving each placeholder back to the stored
    /// value it names.
    ///
    /// A placeholder naming a position that no longer exists is dropped rather than
    /// written through: the literal text is not a secret and storing it would make a
    /// placeholder look like a working credential.
    /// </summary>
    private static void MergeSecretArray(
        System.Text.Json.Utf8JsonWriter writer,
        JsonElement incoming,
        JsonElement existing)
    {
        var stored = existing.ValueKind == JsonValueKind.Array
            ? existing.EnumerateArray().ToArray()
            : [];

        writer.WriteStartArray();
        foreach (var element in incoming.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String &&
                TryReadRedactedElement(element.GetString(), out var index))
            {
                if (index < stored.Length)
                {
                    stored[index].WriteTo(writer);
                }

                continue;
            }

            element.WriteTo(writer);
        }

        writer.WriteEndArray();
    }

    public static object ToDownloadClientSummaryResponse(DownloadClientConfiguration config)
    {
        return new
        {
            config.Id,
            config.Name,
            config.Type,
            config.Host,
            config.Port,
            config.Username,
            config.UseSSL,
            config.IsEnabled,
            config.RemoveCompletedDownloads,
            Settings = config.Settings,
            config.CreatedAt
        };
    }

    public static object ToDownloadClientDetailResponse(DownloadClientConfiguration config)
    {
        return new
        {
            config.Id,
            config.Name,
            config.Type,
            config.Host,
            config.Port,
            config.Username,
            config.UseSSL,
            config.IsEnabled,
            config.RemoveCompletedDownloads,
            Settings = config.Settings,
            config.CreatedAt
        };
    }

    private static string RedactJsonSettings(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return RedactedValue;

            using var ms = new System.IO.MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (IsSensitiveKey(prop.Name) &&
                        prop.Value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                    {
                        writer.WriteString(prop.Name, RedactedValue);
                    }
                    else if (IsSensitiveKey(prop.Name) && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        // A list of secrets — several API keys, say — is redacted element
                        // by element. Writing the array through because it is not a string
                        // would hand every one of them back in the clear, and replacing
                        // the whole array with one placeholder would lose which element is
                        // which, so each gets a placeholder naming its position. The merge
                        // below reads those back, which is what lets a caller remove or
                        // reorder the list without destroying the entries it kept.
                        writer.WritePropertyName(prop.Name);
                        writer.WriteStartArray();
                        var index = 0;
                        foreach (var element in prop.Value.EnumerateArray())
                        {
                            if (element.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(element.GetString()))
                            {
                                writer.WriteStringValue(RedactedElement(index));
                            }
                            else
                            {
                                element.WriteTo(writer);
                            }

                            index++;
                        }

                        writer.WriteEndArray();
                    }
                    else
                    {
                        writer.WritePropertyName(prop.Name);
                        prop.Value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (JsonException)
        {
            return RedactedValue;
        }
    }

    private static Dictionary<string, string> RedactStringDictionary(Dictionary<string, string>? input)
    {
        if (input == null) return new Dictionary<string, string>();
        var clone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in input)
        {
            clone[kvp.Key] = IsSensitiveKey(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value)
                ? RedactedValue
                : kvp.Value;
        }
        return clone;
    }

    private static Dictionary<string, object> RedactObjectDictionary(Dictionary<string, object>? input)
    {
        if (input == null) return new Dictionary<string, object>();
        var clone = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in input)
        {
            if (IsSensitiveKey(kvp.Key))
            {
                clone[kvp.Key] = RedactedValue;
                continue;
            }

            clone[kvp.Key] = kvp.Value;
        }
        return clone;
    }

    private static bool IsSensitiveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var k = key.Trim().ToLowerInvariant();
        return k.Contains("apikey")
               || k.Contains("api_key")
               || k.Contains("token")
               || k.Contains("secret")
               || k.Contains("password")
               || k.Contains("mam")
               || k.Contains("cookie")
               || k.Contains("authorization");
    }

    private static T Clone<T>(T value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException($"Failed to clone {typeof(T).Name}");
    }
}
