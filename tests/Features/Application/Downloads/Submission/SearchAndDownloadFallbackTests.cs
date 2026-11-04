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

using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Submission
{
    /// <summary>
    /// What happens when a release an indexer lists turns out not to be fetchable.
    ///
    /// Listed is not the same as available: an abook.link topic whose payload no index
    /// holds throws at submission. That used to end the search, and the book was left
    /// holding a failed row nobody would ever clear - the search had found something, so
    /// "nothing obtainable" was false, and the grab had thrown, so nothing was sent
    /// either. Measured on 'Disturb' (audiobook 4904) on 2026-11-04.
    /// </summary>
    [Trait("Name", "SearchAndDownloadFallbackTests")]
    [Trait("Category", "Downloads")]
    public sealed class SearchAndDownloadFallbackTests : BaseTests
    {
        private const string FirstMagnet = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        private const string SecondMagnet = "magnet:?xt=urn:btih:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        private static SearchResult Release(string title, string magnet) => new()
        {
            Title = title,
            Artist = "Andy Weir",
            DownloadType = "Torrent",
            MagnetLink = magnet,
            Size = 123_456_789
        };

        private Mock<IDownloadClientGateway> _gateway = new();

        /// <summary>
        /// Two releases, scored equally acceptable, in a fixed order. The scorer is
        /// stubbed so the test is about the fallback rather than about scoring.
        /// </summary>
        private async Task<Audiobook> ArrangeAsync(params SearchResult[] releases)
        {
            var search = new Mock<ISearchService>();
            search
                .Setup(s => s.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<List<string>?>(),
                    It.IsAny<SearchSortBy>(),
                    It.IsAny<SearchSortDirection>(),
                    It.IsAny<bool>()))
                .ReturnsAsync(releases.ToList());

            var profiles = new Mock<IQualityProfileService>();
            profiles
                .Setup(p => p.ScoreSearchResults(It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
                .ReturnsAsync((List<SearchResult> results, QualityProfile _) => results
                    .Select((result, index) => new QualityScore
                    {
                        SearchResult = result,
                        // Descending, so the order the test writes them is the order tried.
                        TotalScore = 100 - index
                    })
                    .ToList());

            _services.AddSingleton(search.Object);
            _services.AddSingleton(profiles.Object);
            _services.AddSingleton(_gateway.Object);
            Init();

            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(Path.GetTempPath())
                .Build());
            await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfiguration
            {
                Id = "qb-1",
                Name = "local qbit",
                Type = "qbittorrent",
                Host = "localhost",
                Port = 8080,
                IsEnabled = true
            });

            return await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithBasePath(FileService.GetTempPath())
                .WithQualityProfile(new QualityProfile { Id = 1, Name = "Any" })
                .Build());
        }

        private void Refuses(string magnet, string why) =>
            _gateway
                .Setup(g => g.AddAsync(
                    It.IsAny<DownloadClientConfiguration>(),
                    It.Is<PreparedDownloadSubmission>(submission => submission.OriginalLocator == magnet),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DownloadClientSubmissionException(why));

        private void Accepts(string magnet) =>
            _gateway
                .Setup(g => g.AddAsync(
                    It.IsAny<DownloadClientConfiguration>(),
                    It.Is<PreparedDownloadSubmission>(submission => submission.OriginalLocator == magnet),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DownloadClientSubmissionResult("qb-ext-1", null, false));

        [Fact]
        public async Task TheNextReleaseIsTriedWhenTheFirstCannotBeFetched()
        {
            var audiobook = await ArrangeAsync(
                Release("Artemis - the one no index holds", FirstMagnet),
                Release("Artemis - the one that works", SecondMagnet));
            Refuses(FirstMagnet, "Resolve NZB: No index holds this release.");
            Accepts(SecondMagnet);

            var result = await _provider.GetRequiredService<DownloadService>()
                .SearchAndDownloadAsync(audiobook.Id);

            Assert.True(result.Success);
            Assert.Equal("Artemis - the one that works", result.SearchResult?.Title);
            Assert.False(result.NothingObtainable);
        }

        [Fact]
        public async Task NothingObtainableWhenNoReleaseCanBeFetched()
        {
            // The case that left a row stuck. Found, so "no results" is wrong; unfetchable,
            // so nothing was sent - and without this the book said neither, and its failure
            // stayed in the queue for ever.
            var audiobook = await ArrangeAsync(
                Release("Artemis - listed nowhere fetchable", FirstMagnet),
                Release("Artemis - also not fetchable", SecondMagnet));
            Refuses(FirstMagnet, "Resolve NZB: No index holds this release.");
            Refuses(SecondMagnet, "Resolve NZB: No index holds this release.");

            var result = await _provider.GetRequiredService<DownloadService>()
                .SearchAndDownloadAsync(audiobook.Id);

            Assert.False(result.Success);
            Assert.True(result.NothingObtainable);
            Assert.Contains("could be fetched", result.Message);
            // and it names the refusal, so the log says which wall it hit.
            Assert.Contains("No index holds this release", result.Message);
        }
    }
}
