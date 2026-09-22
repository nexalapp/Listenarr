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

namespace Listenarr.Tests.Features.Application.Audiobooks.Catalog
{
    /// <summary>
    /// What the library list carries. It is deliberately slimmer than a book's own page,
    /// but everything a card shows has to be in it: a field left out is a feature that
    /// silently does nothing, which is how the rating badge came to show on no cover.
    /// </summary>
    [Trait("Name", "LibraryListServiceTests")]
    [Trait("Category", "Library")]
    public sealed class LibraryListServiceTests : BaseTests
    {
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly Mock<IAudiobookFileRepository> _files = new();
        private readonly Mock<IQualityProfileRepository> _profiles = new();
        private readonly Mock<IDownloadRepository> _downloads = new();

        private LibraryListService BuildService(params Audiobook[] library)
        {
            _audiobooks.Setup(r => r.GetAllAsync()).ReturnsAsync(library.ToList());
            _audiobooks
                .Setup(r => r.GetAllSeriesMembershipsGroupedByAudiobookIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<int, List<AudiobookSeriesMembership>>());
            _files.Setup(r => r.GetFormatSummariesAsync()).ReturnsAsync([]);
            _files.Setup(r => r.GetCountsByAudiobookIdAsync()).ReturnsAsync(new Dictionary<int, int>());
            _files
                .Setup(r => r.GetWorstChapterHealthByAudiobookIdAsync())
                .ReturnsAsync(new Dictionary<int, AudiobookChapterSummary>());
            _files.Setup(r => r.GetNotFoundCountsByAudiobookIdAsync()).ReturnsAsync(new Dictionary<int, int>());
            _profiles.Setup(r => r.GetAllAsync()).ReturnsAsync([]);
            _downloads
                .Setup(r => r.GetActiveAudiobookIdsAsync(It.IsAny<DownloadStatus[]>()))
                .ReturnsAsync([]);

            return new LibraryListService(_audiobooks.Object, _files.Object, _profiles.Object, _downloads.Object);
        }

        [Fact]
        public async Task GetAllAsync_CarriesTheRatingACardShows()
        {
            var book = new Audiobook
            {
                Id = 1,
                Title = "Project Hail Mary",
                AudibleRatingOverall = 4.87,
                AudibleRatingOverallCount = 312_384,
            };

            var row = Assert.Single(await BuildService(book).GetAllAsync());

            Assert.Equal(4.87, row.AudibleRatingOverall);
            Assert.Equal(312_384, row.AudibleRatingOverallCount);
        }

        [Fact]
        public async Task GetAllAsync_CarriesTheAudnexusRatingWhenThatIsTheOneThereIs()
        {
            var book = new Audiobook { Id = 2, Title = "Earthlight", AudnexusRating = 4.4 };

            var row = Assert.Single(await BuildService(book).GetAllAsync());

            Assert.Equal(4.4, row.AudnexusRating);
            Assert.Null(row.AudibleRatingOverall);
        }

        [Fact]
        public async Task GetAllAsync_LeavesTheRatingEmptyForABookNobodyHasRated()
        {
            var book = new Audiobook { Id = 3, Title = "Archangel Down" };

            var row = Assert.Single(await BuildService(book).GetAllAsync());

            Assert.Null(row.AudibleRatingOverall);
            Assert.Null(row.AudibleRatingOverallCount);
            Assert.Null(row.AudnexusRating);
        }
    }
}
