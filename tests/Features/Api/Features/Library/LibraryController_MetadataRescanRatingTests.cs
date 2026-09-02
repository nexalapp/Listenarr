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
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    /// <summary>
    /// Ratings across a metadata rescan.
    ///
    /// <para>
    /// Ratings are the only fields a rescan touches that change without anyone editing the
    /// book, so a rescan refreshing them is the point rather than a risk — and unlike every
    /// other refreshed field they carry no lock, because nothing in the edit form sets them
    /// and there is therefore no hand-entered value to destroy.
    /// </para>
    /// <para>
    /// Tested end to end rather than against the patch helper because the risk is in how the
    /// write is gated, not in the assignment: a rescan answered by a provider that has no
    /// ratings must leave the stored ones alone instead of half-nulling them, and only the
    /// real provider-selection path shows whether it does.
    /// </para>
    /// </summary>
    [Trait("Name", "LibraryController_MetadataRescanRatingTests")]
    [Trait("Category", "Library")]
    public sealed class LibraryController_MetadataRescanRatingTests
        : BaseTests, IClassFixture<ListenarrWebApplicationFactory>
    {
        private readonly ListenarrWebApplicationFactory _factory;

        public LibraryController_MetadataRescanRatingTests(ListenarrWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task RescanMetadata_RefreshesRatingsThatHaveMovedSinceTheLastScan()
        {
            const string asin = "B0RATEFRSH";

            var factory = WithMetadata(asin, new AudibleBookResponse
            {
                Asin = asin,
                Title = "Project Hail Mary",
                Rating = new AudibleRating
                {
                    NumReviews = 47698,
                    Overall = new AudibleRatingDistribution { AverageRating = 4.8746, NumRatings = 310988 },
                    Performance = new AudibleRatingDistribution { AverageRating = 4.9273, NumRatings = 289538 },
                    Story = new AudibleRatingDistribution { AverageRating = 4.8490, NumRatings = 288987 }
                }
            });

            var audiobookId = await SeedAsync(factory, book =>
            {
                book.Asin = asin;
                book.Title = "Project Hail Mary";

                // A stale snapshot from an earlier scan, with far fewer ratings behind it.
                book.AudibleRatingOverall = 4.5;
                book.AudibleRatingOverallCount = 1200;
                book.AudibleRatingPerformance = 4.4;
                book.AudibleRatingStory = 4.6;
                book.AudibleReviewCount = 90;
            });

            await RescanAsync(factory, audiobookId);

            var updated = await LoadAsync(factory, audiobookId);
            Assert.Equal(4.8746, updated.AudibleRatingOverall);
            Assert.Equal(310988, updated.AudibleRatingOverallCount);
            Assert.Equal(4.9273, updated.AudibleRatingPerformance);
            Assert.Equal(289538, updated.AudibleRatingPerformanceCount);
            Assert.Equal(4.8490, updated.AudibleRatingStory);
            Assert.Equal(288987, updated.AudibleRatingStoryCount);
            Assert.Equal(47698, updated.AudibleReviewCount);
        }

        [Fact]
        public async Task RescanMetadata_LeavesAudibleRatingsAloneWhenTheProviderHasNone()
        {
            const string asin = "B0RATEKEEP";

            // A provider that answered with everything except ratings — an Audnexus answer,
            // or an Audible response fetched without the rating group. Writing the block
            // unconditionally here would erase a book's entire rating history on a rescan
            // that was only meant to correct its title.
            var factory = WithMetadata(asin, new AudibleBookResponse
            {
                Asin = asin,
                Title = "Corrected Title",
                AudnexusRating = 4.9
            });

            var audiobookId = await SeedAsync(factory, book =>
            {
                book.Asin = asin;
                book.Title = "Stale Title";
                book.AudibleRatingOverall = 4.8746;
                book.AudibleRatingOverallCount = 310988;
                book.AudibleRatingPerformance = 4.9273;
                book.AudibleRatingPerformanceCount = 289538;
                book.AudibleRatingStory = 4.8490;
                book.AudibleRatingStoryCount = 288987;
                book.AudibleReviewCount = 47698;
            });

            await RescanAsync(factory, audiobookId);

            var updated = await LoadAsync(factory, audiobookId);

            // The rescan did its job on the field the provider had...
            Assert.Equal("Corrected Title", updated.Title);

            // ...and left every Audible rating standing.
            Assert.Equal(4.8746, updated.AudibleRatingOverall);
            Assert.Equal(310988, updated.AudibleRatingOverallCount);
            Assert.Equal(4.9273, updated.AudibleRatingPerformance);
            Assert.Equal(289538, updated.AudibleRatingPerformanceCount);
            Assert.Equal(4.8490, updated.AudibleRatingStory);
            Assert.Equal(288987, updated.AudibleRatingStoryCount);
            Assert.Equal(47698, updated.AudibleReviewCount);

            // The Audnexus value lands in its own column rather than displacing any of them.
            Assert.Equal(4.9, updated.AudnexusRating);
        }

        private WebApplicationFactory<Program> WithMetadata(string asin, AudibleBookResponse response)
        {
            var metadataMock = new Mock<IAudiobookMetadataService>();
            metadataMock
                .Setup(service => service.GetMetadataAsync(asin, "us", false))
                .ReturnsAsync(new
                {
                    metadata = response,
                    source = "Audible",
                    sourceUrl = "https://audible.com"
                });

            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IAudiobookMetadataService>();
                    services.AddSingleton(metadataMock.Object);
                });
            });
        }

        private static async Task<int> SeedAsync(
            WebApplicationFactory<Program> factory,
            Action<Audiobook> configure)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();

            var audiobook = new Audiobook { Monitored = true };
            configure(audiobook);

            db.Audiobooks.Add(audiobook);
            await db.SaveChangesAsync();
            return audiobook.Id;
        }

        private static async Task<Audiobook> LoadAsync(
            WebApplicationFactory<Program> factory,
            int audiobookId)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
            return await db.Audiobooks.AsNoTracking().FirstAsync(book => book.Id == audiobookId);
        }

        private static async Task RescanAsync(
            WebApplicationFactory<Program> factory,
            int audiobookId)
        {
            var client = factory.CreateClient();

            var tokenResponse = await client.GetAsync("/api/v1/antiforgery/token");
            tokenResponse.EnsureSuccessStatusCode();
            using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
            var csrfToken = tokenJson.RootElement.GetProperty("token").GetString();

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/library/{audiobookId}/rescan-metadata");
            request.Headers.Add("X-XSRF-TOKEN", csrfToken);

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.IsSuccessStatusCode,
                $"Expected success but got {(int)response.StatusCode}: {body}");
        }
    }
}
