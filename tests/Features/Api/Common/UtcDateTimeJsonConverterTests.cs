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
using Listenarr.Api.Common;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Common
{
    /// <summary>
    /// Every time the API sends is an instant with a "Z", whatever kind the store
    /// gave it, and every time it receives without an offset is taken as UTC. The
    /// case that found this: a token due in six minutes shown as six hours, because
    /// an unspecified DateTime went out with no offset and the browser read it as
    /// local time.
    /// </summary>
    [Trait("Name", "UtcDateTimeJsonConverterTests")]
    [Trait("Category", "Api")]
    public sealed class UtcDateTimeJsonConverterTests : BaseTests
    {
        private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcDateTimeJsonConverter() } };

        private sealed record Payload(DateTime At, DateTime? Maybe);

        [Fact]
        public void AnUnspecifiedTime_IsWrittenAsTheUtcItIs()
        {
            var unspecified = new DateTime(2026, 9, 21, 13, 52, 42, DateTimeKind.Unspecified);

            var json = JsonSerializer.Serialize(new Payload(unspecified, unspecified), Options);

            Assert.Contains("\"At\":\"2026-09-21T13:52:42Z\"", json);
            Assert.Contains("\"Maybe\":\"2026-09-21T13:52:42Z\"", json);
        }

        [Fact]
        public void AUtcTime_IsWrittenWithZ_AndALocalTime_IsConvertedFirst()
        {
            var utc = new DateTime(2026, 9, 21, 13, 52, 42, DateTimeKind.Utc);
            var local = utc.ToLocalTime();

            Assert.Contains("\"At\":\"2026-09-21T13:52:42Z\"", JsonSerializer.Serialize(new Payload(utc, null), Options));
            Assert.Contains("\"At\":\"2026-09-21T13:52:42Z\"", JsonSerializer.Serialize(new Payload(local, null), Options));
        }

        [Fact]
        public void ATimeWithNoOffset_IsReadAsUtc_AndOneWithAnOffset_IsMovedToUtc()
        {
            var bare = JsonSerializer.Deserialize<Payload>("{\"At\":\"2026-09-21T13:52:42\",\"Maybe\":null}", Options)!;
            var offset = JsonSerializer.Deserialize<Payload>("{\"At\":\"2026-09-21T07:52:42-06:00\",\"Maybe\":null}", Options)!;

            Assert.Equal(DateTimeKind.Utc, bare.At.Kind);
            Assert.Equal(new DateTime(2026, 9, 21, 13, 52, 42), bare.At);
            Assert.Equal(DateTimeKind.Utc, offset.At.Kind);
            Assert.Equal(new DateTime(2026, 9, 21, 13, 52, 42), offset.At);
            Assert.Null(bare.Maybe);
        }

        [Fact]
        public void ARoundTrip_KeepsTheInstant()
        {
            var original = new DateTime(2026, 9, 21, 13, 52, 42, 123, DateTimeKind.Utc);

            var back = JsonSerializer.Deserialize<Payload>(JsonSerializer.Serialize(new Payload(original, null), Options), Options)!;

            Assert.Equal(original, back.At);
            Assert.Equal(DateTimeKind.Utc, back.At.Kind);
        }
    }
}
