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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    /// <summary>
    /// A metadata rescan against a book with locked fields.
    ///
    /// <para>
    /// Tested end to end rather than against the patch helper, because the guard has to
    /// hold in two places: the field assignment and, for the cover, the image download that
    /// runs after the record is already saved. A unit test of the patch would pass while
    /// the second one overwrote the cover a moment later.
    /// </para>
    /// </summary>
    [Trait("Name", "LibraryController_MetadataRescanLockTests")]
    [Trait("Category", "Library")]
    public sealed class LibraryController_MetadataRescanLockTests
        : BaseTests, IClassFixture<ListenarrWebApplicationFactory>
    {
        private const string Asin = "B0LOCKTEST";

        private readonly ListenarrWebApplicationFactory _factory;

        public LibraryController_MetadataRescanLockTests(ListenarrWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task RescanMetadata_LeavesLockedFieldsAloneAndRefreshesTheRest()
        {
            var metadataMock = new Mock<IAudiobookMetadataService>();
            metadataMock
                .Setup(service => service.GetMetadataAsync(Asin, "us", false))
                .ReturnsAsync(new
                {
                    metadata = new AudibleBookResponse
                    {
                        Asin = Asin,
                        Title = "Provider Title",
                        Subtitle = "Provider Subtitle",
                        Description = "The provider's blurb.",
                        Publisher = "Provider Publisher",
                        Authors = new List<AudibleAuthor> { new() { Name = "Provider Author" } },
                        Narrators = new List<AudibleNarrator> { new() { Name = "Provider Narrator" } }
                    },
                    source = "Audible",
                    sourceUrl = "https://audible.com"
                });

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IAudiobookMetadataService>();
                    services.AddSingleton(metadataMock.Object);
                });
            });

            int audiobookId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
                var audiobook = new Audiobook
                {
                    Title = "My Corrected Title",
                    Subtitle = "My Corrected Subtitle",
                    Description = "The blurb I wrote by hand.",
                    Publisher = "Stale Publisher",
                    Authors = ["Stale Author"],
                    Narrators = ["My Corrected Narrator"],
                    Asin = Asin,
                    Monitored = true,

                    // Three locked, three not. A rescan that respected all of them or none
                    // of them would pass a test that only checked one side.
                    LockedFields =
                    [
                        LockableFields.Title,
                        LockableFields.Description,
                        LockableFields.Narrators
                    ]
                };

                db.Audiobooks.Add(audiobook);
                await db.SaveChangesAsync();
                audiobookId = audiobook.Id;
            }

            var client = factory.CreateClient();
            var response = await PostRescanAsync(client, audiobookId);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.IsSuccessStatusCode,
                $"Expected success but got {(int)response.StatusCode}: {body}");

            using (var json = JsonDocument.Parse(body))
            {
                var kept = json.RootElement.GetProperty("keptFields")
                    .EnumerateArray()
                    .Select(element => element.GetString())
                    .ToList();

                // Named, so a rescan that looks like it did nothing says why.
                Assert.Equal(["Title", "Description", "Narrators"], kept);
            }

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
                var updated = await db.Audiobooks.FirstAsync(book => book.Id == audiobookId);

                Assert.Equal("My Corrected Title", updated.Title);
                Assert.Equal("The blurb I wrote by hand.", updated.Description);
                Assert.Equal(["My Corrected Narrator"], updated.Narrators);

                // Everything unlocked still refreshes: a lock pins one field, it does not
                // turn the rescan off.
                Assert.Equal("Provider Subtitle", updated.Subtitle);
                Assert.Equal("Provider Publisher", updated.Publisher);
                Assert.Equal(["Provider Author"], updated.Authors);

                // And the locks survive the rescan, or they would protect a book once.
                Assert.Equal(
                    [LockableFields.Title, LockableFields.Description, LockableFields.Narrators],
                    LockableFields.Normalize(updated.LockedFields));
            }
        }

        private static async Task<HttpResponseMessage> PostRescanAsync(
            HttpClient client,
            int audiobookId)
        {
            var csrfToken = await GetAntiforgeryTokenAsync(client);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/library/{audiobookId}/rescan-metadata");
            request.Headers.Add("X-XSRF-TOKEN", csrfToken);
            return await client.SendAsync(request);
        }

        private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
        {
            var tokenResponse = await client.GetAsync("/api/v1/antiforgery/token");
            tokenResponse.EnsureSuccessStatusCode();
            using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
            var csrfToken = tokenJson.RootElement.GetProperty("token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(csrfToken));
            return csrfToken!;
        }
    }
}
