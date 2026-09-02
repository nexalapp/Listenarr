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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Metadata
{
    /// <summary>
    /// Ratings on the auto-populate path, which fills a book that arrived empty and is
    /// documented never to overwrite.
    ///
    /// <para>
    /// Ratings are the one kind of field here that moves on its own, so the temptation is to
    /// let this path refresh them. It must not: that would quietly make this method a
    /// writer, and the next field added in the same spirit would overwrite something a
    /// person typed. Refreshing a rating is a rescan's job.
    /// </para>
    /// </summary>
    [Trait("Name", "AudiobookMetadataRefreshRatingTests")]
    [Trait("Category", "Metadata")]
    public class AudiobookMetadataRefreshRatingTests : BaseTests
    {
        private static AudibleBookMetadata Rated() => new()
        {
            AudibleRatingOverall = 4.87,
            AudibleRatingOverallCount = 310988,
            AudibleRatingPerformance = 4.93,
            AudibleRatingPerformanceCount = 289538,
            AudibleRatingStory = 4.85,
            AudibleRatingStoryCount = 288987,
            AudibleReviewCount = 47698
        };

        [Fact]
        public void FillMissingFields_PopulatesRatingsOnABookThatHasNone()
        {
            var audiobook = new Audiobook { Title = "Project Hail Mary" };

            Assert.True(AudiobookMetadataRefreshService.FillMissingFields(audiobook, Rated()));

            Assert.Equal(4.87, audiobook.AudibleRatingOverall);
            Assert.Equal(310988, audiobook.AudibleRatingOverallCount);
            Assert.Equal(4.93, audiobook.AudibleRatingPerformance);
            Assert.Equal(289538, audiobook.AudibleRatingPerformanceCount);
            Assert.Equal(4.85, audiobook.AudibleRatingStory);
            Assert.Equal(288987, audiobook.AudibleRatingStoryCount);
            Assert.Equal(47698, audiobook.AudibleReviewCount);
        }

        [Fact]
        public void FillMissingFields_LeavesRatingsTheBookAlreadyHas()
        {
            var audiobook = new Audiobook
            {
                Title = "Project Hail Mary",
                AudibleRatingOverall = 4.5,
                AudibleRatingOverallCount = 100
            };

            AudiobookMetadataRefreshService.FillMissingFields(audiobook, Rated());

            Assert.Equal(4.5, audiobook.AudibleRatingOverall);
            Assert.Equal(100, audiobook.AudibleRatingOverallCount);
        }

        [Fact]
        public void FillMissingFields_DoesNotReportAChangeWhenTheProviderHasNoRatings()
        {
            var audiobook = new Audiobook { Title = "Unrated", Description = "Already set." };

            // An unrated book must not count as a reason to write the row back, or every
            // auto-populate pass would save a book it changed nothing on.
            Assert.False(AudiobookMetadataRefreshService.FillMissingFields(
                audiobook,
                new AudibleBookMetadata { Description = "Provider blurb." }));

            Assert.Null(audiobook.AudibleRatingOverall);
        }

        [Fact]
        public void FillMissingFields_FillsTheAudnexusRatingIndependently()
        {
            // The fallback fills on its own: a book can have an Audnexus rating and no
            // Audible one, which is exactly the case the column exists for.
            var audiobook = new Audiobook { Title = "Audnexus Only" };

            Assert.True(AudiobookMetadataRefreshService.FillMissingFields(
                audiobook,
                new AudibleBookMetadata { AudnexusRating = 4.9 }));

            Assert.Equal(4.9, audiobook.AudnexusRating);
            Assert.Null(audiobook.AudibleRatingOverall);
        }
    }
}
