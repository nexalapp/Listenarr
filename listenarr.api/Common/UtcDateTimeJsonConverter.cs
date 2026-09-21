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
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Listenarr.Api.Common
{
    /// <summary>
    /// Every time the API sends or receives is an instant in UTC.
    ///
    /// The application keeps time in UTC throughout, but a value read back from
    /// SQLite has no kind, and System.Text.Json writes an unspecified DateTime with
    /// no offset - "2026-09-21T13:52:42" - which a browser reads as local time. On a
    /// machine six zones west of UTC a token due in six minutes showed as six hours.
    /// So: on the way out, an unspecified time is taken as the UTC it is, a local
    /// one is converted, and both are written with a "Z"; on the way in, a string
    /// with no offset is likewise taken as UTC.
    /// </summary>
    public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new JsonException("A date is required.");
            }

            var parsed = DateTime.Parse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            var utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
            writer.WriteStringValue(utc);
        }
    }
}
