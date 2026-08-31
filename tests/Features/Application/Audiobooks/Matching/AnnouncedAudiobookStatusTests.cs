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

namespace Listenarr.Tests.Features.Application.Audiobooks.Matching
{
    [Trait("Name", "AnnouncedAudiobookStatusTests")]
    [Trait("Category", "AudiobookStatusEvaluator")]
    public class AnnouncedAudiobookStatusTests : BaseTests
    {
        private static readonly DateOnly Today = new(2026, 8, 31);

        [Fact]
        public void ComputeStatus_ReturnsAnnounced_ForAFilelessBookWithAFutureDate()
        {
            // When
            var status = AudiobookStatusEvaluator.ComputeStatus(
                isDownloading: false,
                hasAnyFile: false,
                audiobookQuality: null,
                qualityProfile: null,
                files: null,
                publishedDate: "2028-01-11",
                today: Today);

            // Then
            Assert.Equal(AudiobookStatusEvaluator.Announced, status);
        }

        [Fact]
        public void ComputeStatus_SeparatesAnnouncedFromMerelyWanted()
        {
            // Given two books with no file, differing only in when they come out
            var announced = AudiobookStatusEvaluator.ComputeStatus(
                false, false, null, null, null, publishedDate: "2027-04-06", today: Today);
            var wanted = AudiobookStatusEvaluator.ComputeStatus(
                false, false, null, null, null, publishedDate: "2021-01-28", today: Today);

            // Then they no longer collapse into the same status
            Assert.Equal(AudiobookStatusEvaluator.Announced, announced);
            Assert.Equal(AudiobookStatusEvaluator.NoFile, wanted);
            Assert.NotEqual(announced, wanted);
        }

        [Theory]
        [InlineData("2027-04")]
        [InlineData("2027")]
        public void ComputeStatus_ReturnsAnnounced_ForImpreciseFutureDates(string publishedDate)
        {
            // A month-only or year-only announcement is still an announcement.
            var status = AudiobookStatusEvaluator.ComputeStatus(
                false, false, null, null, null, publishedDate: publishedDate, today: Today);

            Assert.Equal(AudiobookStatusEvaluator.Announced, status);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("2026-08-31")]
        [InlineData("2019-11-02")]
        [InlineData("nonsense")]
        public void ComputeStatus_ReturnsNoFile_WhenTheDateIsAbsentPastOrUnreadable(string? publishedDate)
        {
            var status = AudiobookStatusEvaluator.ComputeStatus(
                false, false, null, null, null, publishedDate: publishedDate, today: Today);

            Assert.Equal(AudiobookStatusEvaluator.NoFile, status);
        }

        [Fact]
        public void ComputeStatus_PrefersDownloading_OverAnnounced()
        {
            // An active grab is a stronger statement about the book than its release date.
            var status = AudiobookStatusEvaluator.ComputeStatus(
                isDownloading: true,
                hasAnyFile: false,
                audiobookQuality: null,
                qualityProfile: null,
                files: null,
                publishedDate: "2028-01-11",
                today: Today);

            Assert.Equal(AudiobookStatusEvaluator.Downloading, status);
        }

        [Fact]
        public void ComputeStatus_NeverAnnouncesABookThatAlreadyHasAFile()
        {
            // A future date on a book already on disk is bad metadata, not an announcement;
            // announced is strictly a refinement of "no file".
            var status = AudiobookStatusEvaluator.ComputeStatus(
                isDownloading: false,
                hasAnyFile: true,
                audiobookQuality: null,
                qualityProfile: null,
                files: null,
                publishedDate: "2028-01-11",
                today: Today);

            Assert.Equal(AudiobookStatusEvaluator.QualityMatch, status);
        }

        [Fact]
        public void ComputeStatus_KeepsItsOldBehaviour_WhenNoDateIsSupplied()
        {
            // Callers that never pass a date must be unaffected by the new parameter.
            var status = AudiobookStatusEvaluator.ComputeStatus(
                isDownloading: false,
                hasAnyFile: false,
                audiobookQuality: null,
                qualityProfile: null,
                files: null);

            Assert.Equal(AudiobookStatusEvaluator.NoFile, status);
        }
    }
}
