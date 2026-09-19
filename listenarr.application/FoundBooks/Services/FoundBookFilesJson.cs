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
using Listenarr.Domain.FoundBooks;

namespace Listenarr.Application.FoundBooks.Services
{
    /// <summary>The one place <see cref="FoundBook.FilesJson"/> is read or written.</summary>
    public static class FoundBookFilesJson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string Serialize(IEnumerable<FoundBookFileEntry> files) =>
            JsonSerializer.Serialize(files.ToList(), Options);

        public static IReadOnlyList<FoundBookFileEntry> Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<FoundBookFileEntry>>(json, Options) ?? [];
            }
            catch (JsonException)
            {
                // A row written by a build with a different shape is not worth failing a
                // scan over; the files are re-probed and the row rewritten.
                return [];
            }
        }
    }
}
