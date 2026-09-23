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
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search
{
    [Trait("Area", "Search")]
    [Trait("Name", "SearchServiceTests")]
    [Trait("Category", "SearchService")]
    public class SearchServiceTests : BaseTests
    {
        [Fact]
        [Trait("Method", "IntelligentSearchAsync")]
        [Trait("Scenario", "AudibleTitleResultUsesRequestedRegionForLinks")]
        public async Task IntelligentSearch_TitleAudibleResult_UsesRequestedRegionForProductLinks()
        {
            // Given
            using var httpClient = new HttpClient();
            var audible = new Mock<AudibleService>(httpClient, NullLogger<AudibleService>.Instance);
            var audibleResult = new AudibleSearchResultBuilder()
                .WithAsin("B0DUNE1234")
                .WithTitle("Dune")
                .WithAuthor("Frank Herbert")
                .WithLanguage("german")
                .Build();
            var audibleResponse = new AudibleSearchResponseBuilder()
                .WithResult(audibleResult)
                .WithTotalResults(1)
                .Build();

            audible
                .Setup(service => service.SearchByTitleAsync("Dune", 1, 50, "de", "german"))
                .ReturnsAsync(audibleResponse);

            Init(services => services
                .WithSingleton<AudibleService>(audible.Object)
                .WithSingleton<IOverDriveService>(new StubOverDriveService()));
            var searchService = _provider.GetRequiredService<ISearchService>();

            // When
            var results = await searchService.IntelligentSearchAsync("TITLE:Dune", region: "de", language: "german");

            // Then
            var result = Assert.Single(results);
            Assert.Equal("https://www.audible.de/pd/B0DUNE1234", result.ProductUrl);
            Assert.Equal("https://www.audible.de/pd/B0DUNE1234", result.SourceLink);
            Assert.Equal("Audible", result.MetadataSource);
            audible.Verify(service => service.SearchByTitleAsync("Dune", 1, 50, "de", "german"), Times.Once);
        }

        [Fact]
        [Trait("Method", "IntelligentSearchAsync")]
        [Trait("Scenario", "AudibleAnswerStillOffersLibraryEditions")]
        public async Task IntelligentSearch_AudibleAnswers_StillOffersLibraryEditions()
        {
            // Given a shop that answers, and a library that lends a different recording.
            using var httpClient = new HttpClient();
            var audible = new Mock<AudibleService>(httpClient, NullLogger<AudibleService>.Instance);
            var audibleResponse = new AudibleSearchResponseBuilder()
                .WithResult(new AudibleSearchResultBuilder()
                    .WithAsin("B0PIMP12345")
                    .WithTitle("The Scarlet Pimpernel")
                    .WithAuthor("Baroness Orczy")
                    .Build())
                .WithTotalResults(1)
                .Build();
            audible
                .Setup(service => service.SearchByTitleAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .ReturnsAsync(audibleResponse);

            var overDrive = new StubOverDriveService(new OverDriveEdition(
                Id: "1234567",
                Title: "The Scarlet Pimpernel",
                Authors: ["Baroness Orczy"],
                Narrators: ["Wanda McCaddon"],
                Publisher: "Tantor Media",
                RuntimeMinutes: 570,
                PublishYear: "2008",
                ImageUrl: null));

            Init(services => services
                .WithSingleton<AudibleService>(audible.Object)
                .WithSingleton<IOverDriveService>(overDrive));
            var searchService = _provider.GetRequiredService<ISearchService>();

            // When
            var results = await searchService.IntelligentSearchAsync("TITLE:The Scarlet Pimpernel");

            // Then the shop's answer does not hide the reader nobody sells.
            Assert.Contains(results, result => result.MetadataSource == "Audible");
            var lent = Assert.Single(results, result => result.MetadataSource == "OverDrive");
            Assert.Equal("Wanda McCaddon", lent.Narrator);
            Assert.Equal("Tantor Media", lent.Publisher);
        }

        /// <summary>An answer from a library catalogue without asking one.</summary>
        private sealed class StubOverDriveService(params OverDriveEdition[] editions) : IOverDriveService
        {
            public Task<IReadOnlyList<OverDriveEdition>> SearchAsync(
                string? title,
                string? author,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<OverDriveEdition>>(editions);
        }
    }
}
