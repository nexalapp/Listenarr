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
using Listenarr.Infrastructure.Ffmpeg.Metadata;

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Ffmpeg.Metadata
{
    [Trait("Name", "FfprobeTagMetadataMapperTests")]
    [Trait("Category", "Infrastructure")]
    public class FfprobeTagMetadataMapperTests : BaseTests
    {
        private static JsonElement TagsFrom(string json)
        {
            // JsonDocument is disposable, but the returned element is only read synchronously
            // within each test, so cloning keeps it valid without keeping the document alive.
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        [Fact]
        public void Apply_ReadsAsinAndIsbnFromTags()
        {
            var metadata = new AudioMetadata();
            var tags = TagsFrom("{\"title\":\"A Book\",\"ASIN\":\"B0078PA1OA\",\"ISBN\":\"9781250120207\"}");

            FfprobeTagMetadataMapper.Apply(metadata, tags);

            Assert.Equal("B0078PA1OA", metadata.Asin);
            Assert.Equal("9781250120207", metadata.Isbn);
            Assert.Equal("A Book", metadata.Title);
        }

        [Fact]
        public void Apply_MatchesAsinTag_CaseInsensitively()
        {
            var metadata = new AudioMetadata();
            var tags = TagsFrom("{\"asin\":\"B0078PA1OA\"}");

            FfprobeTagMetadataMapper.Apply(metadata, tags);

            Assert.Equal("B0078PA1OA", metadata.Asin);
        }

        [Fact]
        public void Apply_ReadsAudibleAsinTagVariant()
        {
            var metadata = new AudioMetadata();
            var tags = TagsFrom("{\"AUDIBLE_ASIN\":\"B0078PA1OA\"}");

            FfprobeTagMetadataMapper.Apply(metadata, tags);

            Assert.Equal("B0078PA1OA", metadata.Asin);
        }

        [Fact]
        public void Apply_LeavesAsinNull_WhenNoAsinTagPresent()
        {
            var metadata = new AudioMetadata();
            var tags = TagsFrom("{\"title\":\"A Book\"}");

            FfprobeTagMetadataMapper.Apply(metadata, tags);

            Assert.Null(metadata.Asin);
            Assert.Null(metadata.Isbn);
        }

        [Fact]
        public void Apply_DoesNotOverwriteExistingAsin()
        {
            var metadata = new AudioMetadata { Asin = "EXISTING123" };
            var tags = TagsFrom("{\"ASIN\":\"B0078PA1OA\"}");

            FfprobeTagMetadataMapper.Apply(metadata, tags);

            Assert.Equal("EXISTING123", metadata.Asin);
        }

        [Fact]
        public void Apply_ReadsDescriptiveTagsUsedByManualImport()
        {
            var metadata = new AudioMetadata();
            var tags = TagsFrom(
                "{\"description\":\"A blurb\",\"composer\":\"Jefferson Mays\","
                + "\"genre\":\"Science Fiction\",\"publisher\":\"Orbit\","
                + "\"SERIES\":\"The Expanse\",\"SERIES-PART\":\"0.1\","
                + "\"Subtitle\":\"The Expanse, Book 0.1\",\"language\":\"eng\"}");

            FfprobeTagMetadataMapper.Apply(metadata, tags);

            Assert.Equal("A blurb", metadata.Description);
            Assert.Equal("Jefferson Mays", metadata.Narrator);
            Assert.Equal("Science Fiction", metadata.Genre);
            Assert.Equal("Orbit", metadata.Publisher);
            Assert.Equal("The Expanse", metadata.Series);
            Assert.Equal(0.1m, metadata.SeriesPosition);
            Assert.Equal("The Expanse, Book 0.1", metadata.Subtitle);
            Assert.Equal("eng", metadata.Language);
        }

        [Fact]
        public void Apply_FallsBackToCommentWhenDescriptionIsAbsent()
        {
            // Most taggers write the blurb to "comment"; only some also write "description",
            // so a file carrying just the comment must still yield a description.
            var metadata = new AudioMetadata();
            var tags = TagsFrom("{\"comment\":\"Only a comment\"}");

            FfprobeTagMetadataMapper.Apply(metadata, tags);

            Assert.Equal("Only a comment", metadata.Description);
        }

        [Fact]
        public void Apply_PrefersDescriptionOverComment()
        {
            var metadata = new AudioMetadata();
            var tags = TagsFrom("{\"comment\":\"comment text\",\"description\":\"description text\"}");

            FfprobeTagMetadataMapper.Apply(metadata, tags);

            Assert.Equal("description text", metadata.Description);
        }

        [Fact]
        public void Apply_ParsesSeriesPositionWithTheInvariantCulture()
        {
            // "1.5" always uses '.' as its decimal separator whatever the server locale is.
            // Parsing it with the ambient culture reads it as 15 under de-DE.
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var metadata = new AudioMetadata();
                var tags = TagsFrom("{\"SERIES-PART\":\"1.5\"}");

                FfprobeTagMetadataMapper.Apply(metadata, tags);

                Assert.Equal(1.5m, metadata.SeriesPosition);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void Apply_LeavesANonNumericSeriesPositionUnset()
        {
            // An omnibus carries a range like "1-4". There is no decimal form of that, so
            // recording a wrong number would be worse than recording nothing.
            var metadata = new AudioMetadata();
            var tags = TagsFrom("{\"SERIES\":\"Father Brown\",\"SERIES-PART\":\"1-4\"}");

            FfprobeTagMetadataMapper.Apply(metadata, tags);

            Assert.Equal("Father Brown", metadata.Series);
            Assert.Null(metadata.SeriesPosition);
        }

        [Fact]
        public void Apply_DoesNotOverwriteDescriptiveValuesAlreadyResolved()
        {
            var metadata = new AudioMetadata { Narrator = "Already Known" };
            var tags = TagsFrom("{\"composer\":\"From File\"}");

            FfprobeTagMetadataMapper.Apply(metadata, tags);

            Assert.Equal("Already Known", metadata.Narrator);
        }

    }
}
