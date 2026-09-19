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
using System.Text.Json.Serialization;
using Listenarr.Domain.Audiobooks.Chapters;

namespace Listenarr.Application.Audiobooks.Chapters
{
    /// <summary>The stored form of a planning outcome, on the file row.</summary>
    public static class ChapterPlanStorage
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public static string Serialize(ChapterPlanOutcome outcome) => JsonSerializer.Serialize(outcome, Json);

        public static ChapterPlanOutcome? Deserialize(string json)
        {
            try
            {
                var outcome = JsonSerializer.Deserialize<ChapterPlanOutcome>(json, Json);
                return outcome is { Plan: null, Rejection: null } ? null : outcome;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
