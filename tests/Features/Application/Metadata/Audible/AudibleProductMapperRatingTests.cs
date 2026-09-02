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
    /// <summary>
    /// Mapping Audible's "rating" response group.
    ///
    /// <para>
    /// The shapes below are trimmed from live responses. The trap this class exists for is
    /// that Audible answers an unrated book with the complete structure zero-filled rather
    /// than omitting it, so a mapper that reads <c>average_rating</c> on its own records a
    /// genuine 0.0 — and a book nobody has rated then sorts and displays below the worst
    /// book in the library. <c>num_ratings</c> is the only thing that separates the two.
    /// </para>
    /// </summary>
    [Trait("Name", "AudibleProductMapperRatingTests")]
    [Trait("Category", "Metadata")]
    public class AudibleProductMapperRatingTests : BaseTests
    {
        /// <summary>Live values for "Project Hail Mary" (B08G9PRS1K).</summary>
        private const string RatedProduct = """
        {
          "asin": "B08G9PRS1K",
          "title": "Project Hail Mary",
          "rating": {
            "num_reviews": 47698,
            "overall_distribution":     { "average_rating": 4.8746125252421315, "display_average_rating": "4.9", "num_ratings": 310988 },
            "performance_distribution": { "average_rating": 4.92736359303442,   "display_average_rating": "4.9", "num_ratings": 289538 },
            "story_distribution":       { "average_rating": 4.849079716388626,  "display_average_rating": "4.8", "num_ratings": 288987 }
          }
        }
        """;

        /// <summary>Live values for an unrated title (B002V0QMKM), zero-filled by Audible.</summary>
        private const string UnratedProduct = """
        {
          "asin": "B002V0QMKM",
          "title": "An Unrated Book",
          "rating": {
            "num_reviews": 0,
            "overall_distribution":     { "average_rating": 0.0, "display_average_rating": "0.0", "num_ratings": 0 },
            "performance_distribution": { "average_rating": 0.0, "display_average_rating": "0.0", "num_ratings": 0 },
            "story_distribution":       { "average_rating": 0.0, "display_average_rating": "0.0", "num_ratings": 0 }
          }
        }
        """;

        private static AudibleBookResponse Map(string productJson)
        {
            using var document = JsonDocument.Parse(productJson);
            var mapped = AudibleProductMapper.MapProductToBookResponse(document.RootElement, "us");
            Assert.NotNull(mapped);
            return mapped!;
        }

        [Fact]
        public void MapProductToBookResponse_KeepsTheThreeDistributionsApart()
        {
            var rating = Map(RatedProduct).Rating;

            Assert.NotNull(rating);

            // Full precision, because rounding is a display decision and 4.87 against 4.93
            // is the whole reason for storing the split.
            Assert.Equal(4.8746125252421315, rating!.Overall!.AverageRating);
            Assert.Equal(4.92736359303442, rating.Performance!.AverageRating);
            Assert.Equal(4.849079716388626, rating.Story!.AverageRating);

            Assert.Equal(310988, rating.Overall.NumRatings);
            Assert.Equal(289538, rating.Performance.NumRatings);
            Assert.Equal(288987, rating.Story.NumRatings);
        }

        [Fact]
        public void MapProductToBookResponse_DoesNotConfuseWrittenReviewsWithStarRatings()
        {
            var rating = Map(RatedProduct).Rating;

            // Two populations, an order of magnitude apart. Reading num_reviews as the
            // rating count would under-report this book by 263,290.
            Assert.Equal(47698, rating!.NumReviews);
            Assert.Equal(310988, rating.Overall!.NumRatings);
            Assert.NotEqual(rating.NumReviews, rating.Overall.NumRatings);
        }

        [Fact]
        public void MapProductToBookResponse_TreatsAnUnratedBookAsUnratedRatherThanZero()
        {
            // Not 0.0: the zero-fill is an absence, and recording it as a score would rank a
            // book nobody has rated below every book anybody disliked.
            Assert.Null(Map(UnratedProduct).Rating);
        }

        [Fact]
        public void MapProductToBookResponse_DropsOnlyTheDistributionThatHasNoRatings()
        {
            // Audible zero-fills per distribution, so a book can carry a real overall score
            // while one of the other two has nothing behind it.
            var rating = Map("""
            {
              "asin": "B0PARTIAL1",
              "title": "Partly Rated",
              "rating": {
                "num_reviews": 3,
                "overall_distribution":     { "average_rating": 4.25, "num_ratings": 12 },
                "performance_distribution": { "average_rating": 0.0,  "num_ratings": 0 },
                "story_distribution":       { "average_rating": 3.5,  "num_ratings": 8 }
              }
            }
            """).Rating;

            Assert.NotNull(rating);
            Assert.Equal(4.25, rating!.Overall!.AverageRating);
            Assert.Null(rating.Performance);
            Assert.Equal(3.5, rating.Story!.AverageRating);
        }

        [Fact]
        public void MapProductToBookResponse_LeavesRatingNullWhenTheResponseGroupIsAbsent()
        {
            // A lookup that did not ask for the rating group, or a stub response. Absent is
            // not the same as unrated, but both come out null and neither is a score.
            var mapped = Map("""{ "asin": "B0NORATING", "title": "No Rating Group" }""");

            Assert.Null(mapped.Rating);
            Assert.Null(mapped.AudnexusRating);
        }
    }
}
