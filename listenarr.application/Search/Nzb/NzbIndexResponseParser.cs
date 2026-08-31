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

namespace Listenarr.Application.Search.Nzb
{
    /// <summary>
    /// Reads NZBIndex's search response.
    ///
    /// The envelope is <c>{"data":{"content":[…],"page":{…}},"error":…}</c>. Fields are
    /// read defensively: a missing one means the index did not say, which is different
    /// from saying zero, and a wrong size or completeness flag would be acted on as fact.
    /// </summary>
    public static class NzbIndexResponseParser
    {
        public static IReadOnlyList<NzbCandidate> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (root.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.True)
                {
                    return [];
                }

                if (!root.TryGetProperty("data", out var data)
                    || !data.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                var results = new List<NzbCandidate>();
                foreach (var item in content.EnumerateArray())
                {
                    var candidate = ReadCandidate(item);
                    if (candidate != null)
                    {
                        results.Add(candidate);
                    }
                }

                return results;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static NzbCandidate? ReadCandidate(JsonElement item)
        {
            var id = ReadString(item, "id");
            var name = ReadString(item, "name");

            if (id is null || name is null)
            {
                return null;
            }

            return new NzbCandidate(
                id,
                name,
                ReadLong(item, "size"),
                ReadInt(item, "fileCount"),
                ReadBool(item, "complete"),
                ReadStrings(item, "groups"),
                ReadString(item, "poster"),
                ReadEpoch(item, "posted"));
        }

        private static string? ReadString(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static long? ReadLong(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed) ? parsed : null;

        private static int? ReadInt(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

        private static bool? ReadBool(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;

        private static DateTime? ReadEpoch(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                : null;

        private static IReadOnlyList<string>? ReadStrings(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return value.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }
    }
}
