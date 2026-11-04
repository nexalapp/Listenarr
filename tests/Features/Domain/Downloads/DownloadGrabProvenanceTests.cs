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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Downloads
{
    /// <summary>
    /// Telling a grab this library made from one a person asked for.
    ///
    /// The distinction decides whether a failure stays in the queue. An automatic grab
    /// has to answer for itself and then go; one made by hand stays, because whoever
    /// made it is tracking that book.
    /// </summary>
    [Trait("Name", "DownloadGrabProvenanceTests")]
    [Trait("Category", "Downloads")]
    public sealed class DownloadGrabProvenanceTests : BaseTests
    {
        private static Download WithMetadata(object? value)
        {
            var download = new Download { Id = "d" };
            if (value != null)
            {
                download.Metadata[DownloadMetadataKeys.AutomaticGrab] = value;
            }

            return download;
        }

        [Fact]
        public void AGrabWithNoProvenanceRecordedReadsAsByHand()
        {
            // Every row written before the flag existed. Wrong in the direction that only
            // ever keeps a failure that might have gone, never hides one.
            Assert.False(WithMetadata(null).GetMetadataBool(DownloadMetadataKeys.AutomaticGrab));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void ABoolSurvivesAsItself(bool stored, bool expected)
        {
            Assert.Equal(expected, WithMetadata(stored).GetMetadataBool(DownloadMetadataKeys.AutomaticGrab));
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        public void ABoolSurvivesTheRoundTripThroughJson(string json, bool expected)
        {
            // Metadata is stored as JSON, so what went in as a bool comes back as a
            // JsonElement. Reading only the CLR bool would have made every grab read as
            // by hand the moment the row was reloaded - which is to say, always.
            var element = JsonSerializer.Deserialize<JsonElement>(json);

            Assert.Equal(expected, WithMetadata(element).GetMetadataBool(DownloadMetadataKeys.AutomaticGrab));
        }

        [Fact]
        public void SomethingThatIsNotABoolAtAllReadsAsByHand()
        {
            Assert.False(WithMetadata("yesterday").GetMetadataBool(DownloadMetadataKeys.AutomaticGrab));
        }
    }
}
