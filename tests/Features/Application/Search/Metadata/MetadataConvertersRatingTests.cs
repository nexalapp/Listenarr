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
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Metadata
{
    /// <summary>
    /// Converting each provider's ratings into the shape the library stores.
    ///
    /// <para>
    /// The two providers do not publish the same thing. Audible gives three distributions
    /// at full precision with counts; Audnexus republishes Audible's overall average
    /// rounded to one decimal, as a string, with no count — verified against the live APIs
    /// for B08G9PRS1K ("4.9" against 4.8746…) and B0036I54I6 ("3.9" against 3.9230…).
    /// Keeping them in separate columns is what stops a stored rating's precision and
    /// provenance depending on which provider happened to answer.
    /// </para>
    /// </summary>
    [Trait("Name", "MetadataConvertersRatingTests")]
    [Trait("Category", "Metadata")]
    public class MetadataConvertersRatingTests : BaseTests
    {
        private static MetadataConverters Converter() =>
            new(imageCacheService: null,
                NullLogger<MetadataConverters>.Instance,
                requestContextAccessor: null);

        [Fact]
        public void ConvertAudibleToMetadata_CarriesEveryDistributionAndBothCounts()
        {
            var metadata = Converter().ConvertAudibleToMetadata(
                new AudibleBookResponse
                {
                    Asin = "B08G9PRS1K",
                    Title = "Project Hail Mary",
                    Rating = new AudibleRating
                    {
                        NumReviews = 47698,
                        Overall = new AudibleRatingDistribution { AverageRating = 4.8746125252421315, NumRatings = 310988 },
                        Performance = new AudibleRatingDistribution { AverageRating = 4.92736359303442, NumRatings = 289538 },
                        Story = new AudibleRatingDistribution { AverageRating = 4.849079716388626, NumRatings = 288987 }
                    }
                },
                "B08G9PRS1K");

            Assert.Equal(4.8746125252421315, metadata.AudibleRatingOverall);
            Assert.Equal(310988, metadata.AudibleRatingOverallCount);
            Assert.Equal(4.92736359303442, metadata.AudibleRatingPerformance);
            Assert.Equal(289538, metadata.AudibleRatingPerformanceCount);
            Assert.Equal(4.849079716388626, metadata.AudibleRatingStory);
            Assert.Equal(288987, metadata.AudibleRatingStoryCount);
            Assert.Equal(47698, metadata.AudibleReviewCount);

            // An Audible answer must not populate the Audnexus column, or the fallback
            // stops meaning "Audible did not answer".
            Assert.Null(metadata.AudnexusRating);
            Assert.True(metadata.HasAudibleRating);
        }

        [Fact]
        public void ConvertAudnexusToMetadata_StoresTheRatingInItsOwnColumn()
        {
            var metadata = Converter().ConvertAudnexusToMetadata(
                new AudnexusBookResponse { Asin = "B08G9PRS1K", Title = "Project Hail Mary", Rating = "4.9" },
                "B08G9PRS1K");

            Assert.Equal(4.9, metadata.AudnexusRating);

            // Nothing Audible-shaped: Audnexus has no counts and no narration/writing split,
            // so filling those would invent data.
            Assert.Null(metadata.AudibleRatingOverall);
            Assert.Null(metadata.AudibleRatingOverallCount);
            Assert.Null(metadata.AudibleRatingPerformance);
            Assert.Null(metadata.AudibleRatingStory);
            Assert.Null(metadata.AudibleReviewCount);

            // And it must not claim to be an Audible rating, because that flag is what
            // decides whether a rescan overwrites Audible's stored distributions.
            Assert.False(metadata.HasAudibleRating);
        }

        [Theory]
        [InlineData("4.9", 4.9)]
        [InlineData("3.9", 3.9)]
        [InlineData("5", 5.0)]
        [InlineData(" 4.5 ", 4.5)]
        public void ParseAudnexusRating_ReadsTheValuesAudnexusPublishes(string raw, double expected)
        {
            Assert.Equal(expected, MetadataConverters.ParseAudnexusRating(raw));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a rating")]
        [InlineData("0")]      // nobody has rated it; an absence, not a score of zero
        [InlineData("0.0")]
        [InlineData("-1")]
        [InlineData("7.5")]    // off the 0-5 scale: the scrape returned something else
        public void ParseAudnexusRating_RejectsAnythingThatIsNotAScore(string? raw)
        {
            // Discarded rather than clamped: a number off the scale means the value is not a
            // rating, and clamping would launder that into a plausible-looking 5.
            Assert.Null(MetadataConverters.ParseAudnexusRating(raw));
        }

        [Fact]
        public void ParseAudnexusRating_ReadsTheSameNumberUnderACommaDecimalCulture()
        {
            // Audnexus always writes '.' as the decimal separator. Parsed under the server's
            // culture, "4.9" becomes 49 wherever '.' is the group separator -- the same trap
            // Audiobook.CreateBasicAudioMetadata documents for series positions. The NAS this
            // fork deploys to can be set to any locale, so this is not hypothetical.
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal(4.9, MetadataConverters.ParseAudnexusRating("4.9"));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void ToAudiobook_CarriesEveryRatingOntoTheStoredBook()
        {
            var audiobook = new AudibleBookMetadata
            {
                Title = "Project Hail Mary",
                AudibleRatingOverall = 4.8746125252421315,
                AudibleRatingOverallCount = 310988,
                AudibleRatingPerformance = 4.92736359303442,
                AudibleRatingPerformanceCount = 289538,
                AudibleRatingStory = 4.849079716388626,
                AudibleRatingStoryCount = 288987,
                AudibleReviewCount = 47698,
                AudnexusRating = 4.9
            }.ToAudiobook();

            Assert.Equal(4.8746125252421315, audiobook.AudibleRatingOverall);
            Assert.Equal(310988, audiobook.AudibleRatingOverallCount);
            Assert.Equal(4.92736359303442, audiobook.AudibleRatingPerformance);
            Assert.Equal(289538, audiobook.AudibleRatingPerformanceCount);
            Assert.Equal(4.849079716388626, audiobook.AudibleRatingStory);
            Assert.Equal(288987, audiobook.AudibleRatingStoryCount);
            Assert.Equal(47698, audiobook.AudibleReviewCount);
            Assert.Equal(4.9, audiobook.AudnexusRating);
        }
    }
}
