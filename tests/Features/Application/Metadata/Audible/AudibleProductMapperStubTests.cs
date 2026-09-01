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

namespace Listenarr.Tests.Features.Application.Metadata.Audible
{
    [Trait("Name", "AudibleProductMapperStubTests")]
    [Trait("Category", "AudibleProductMapper")]
    public class AudibleProductMapperStubTests : BaseTests
    {
        /// <summary>
        /// Exactly what api.audible.com returns for an ASIN it has no product for: HTTP 200,
        /// an "always-returned" response group and three fields. Captured from the live API.
        /// </summary>
        private const string StubProduct =
            """{"asin":"B0DFKPXBWQ","asset_details":[],"is_vvab":false}""";

        private const string RealProduct =
            """
            {
              "asin": "B08G9PRS1K",
              "title": "Project Hail Mary",
              "publisher_name": "Audible Studios",
              "release_date": "2021-05-04",
              "authors": [{ "asin": "B00G0WYW92", "name": "Andy Weir" }],
              "narrators": [{ "name": "Ray Porter" }]
            }
            """;

        private static JsonElement Parse(string json)
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        [Fact]
        public void MapProductToBookResponse_ReturnsNull_ForTheStubAudibleSendsForAnUnknownAsin()
        {
            // When
            var mapped = AudibleProductMapper.MapProductToBookResponse(Parse(StubProduct), "us");

            // Then nothing is produced, so callers report "not found" instead of surfacing a
            // titleless book the UI renders as "Unknown Title".
            Assert.Null(mapped);
        }

        [Theory]
        [InlineData("""{"asin":"B0DFKPXBWQ","title":""}""")]
        [InlineData("""{"asin":"B0DFKPXBWQ","title":"   "}""")]
        [InlineData("""{"asin":"B0DFKPXBWQ","authors":[],"narrators":[]}""")]
        public void MapProductToBookResponse_ReturnsNull_WhenTheTitleIsMissingOrBlank(string json)
        {
            Assert.Null(AudibleProductMapper.MapProductToBookResponse(Parse(json), "us"));
        }

        [Fact]
        public void MapProductToBookResponse_StillReturnsNull_WithoutAnAsin()
        {
            // The pre-existing guard is unchanged.
            Assert.Null(AudibleProductMapper.MapProductToBookResponse(
                Parse("""{"title":"Project Hail Mary"}"""), "us"));
        }

        [Fact]
        public void MapProductToBookResponse_MapsARealProduct_Unchanged()
        {
            // When
            var mapped = AudibleProductMapper.MapProductToBookResponse(Parse(RealProduct), "us");

            // Then a genuine product is unaffected by the guard.
            Assert.NotNull(mapped);
            Assert.Equal("B08G9PRS1K", mapped!.Asin);
            Assert.Equal("Project Hail Mary", mapped.Title);
            Assert.Equal("Audible Studios", mapped.Publisher);
            Assert.Equal("2021-05-04", mapped.ReleaseDate);
            Assert.Equal("Andy Weir", Assert.Single(mapped.Authors!).Name);
            Assert.Equal("Ray Porter", Assert.Single(mapped.Narrators!).Name);
        }

        [Fact]
        public void MapProductToBookResponse_ReturnsNull_WhenTheDocumentIsNotAnObject()
        {
            Assert.Null(AudibleProductMapper.MapProductToBookResponse(Parse("""[]"""), "us"));
        }
    }
}
