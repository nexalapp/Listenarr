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
using System.Net;
using System.Text;
using Listenarr.Infrastructure.Metadata.Providers.OverDrive;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Metadata
{
    [Trait("Name", "OverDriveServiceTests")]
    [Trait("Category", "Metadata")]
    public sealed class OverDriveServiceTests : BaseTests
    {
        /// <summary>The shape the live catalogue answers with, trimmed to what is read.</summary>
        private const string Payload = """
        {
          "items": [
            {
              "id": "1183456",
              "title": "The Scarlet Pimpernel, with eBook",
              "publisher": { "id": "1", "name": "Tantor Media, Inc" },
              "publishDate": "2009-04-13T00:00:00Z",
              "covers": { "cover510Wide": { "href": "https://img1.od-cdn.com/big.jpg" } },
              "creators": [
                { "name": "Baroness Emmuska Orczy", "role": "Author" },
                { "name": "Wanda McCaddon", "role": "Narrator" }
              ],
              "formats": [ { "duration": "08:05:00" } ]
            },
            {
              "id": "999",
              "title": "The Scarlet Pimpernel",
              "publisher": { "name": "Some Press" },
              "creators": [ { "name": "Baroness Orczy", "role": "Author" } ],
              "formats": []
            }
          ]
        }
        """;

        private static OverDriveService ServiceReturning(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var handler = new StubHandler(body, status);
            var client = new HttpClient(handler) { BaseAddress = new Uri("https://thunder.api.overdrive.com/") };
            return new OverDriveService(client, NullLogger<OverDriveService>.Instance);
        }

        [Fact]
        public async Task SearchAsync_ReadsTheReaderTheShopsDoNotList()
        {
            var editions = await ServiceReturning(Payload).SearchAsync("The Scarlet Pimpernel", "Orczy");

            var found = Assert.Single(editions);
            Assert.Equal(["Wanda McCaddon"], found.Narrators);
            Assert.Equal("Tantor Media, Inc", found.Publisher);
            Assert.Equal("2009", found.PublishYear);
            Assert.Equal(485, found.RuntimeMinutes);
            Assert.Equal("https://img1.od-cdn.com/big.jpg", found.ImageUrl);
            Assert.Equal(["Baroness Emmuska Orczy"], found.Authors);
        }

        /// <summary>
        /// An edition crediting nobody is not worth returning: the narrator is the whole
        /// reason to ask a second catalogue, and a nameless row only crowds the picker.
        /// </summary>
        [Fact]
        public async Task SearchAsync_SkipsAnEditionThatNamesNoReader()
        {
            var editions = await ServiceReturning(Payload).SearchAsync("The Scarlet Pimpernel", null);

            Assert.DoesNotContain(editions, e => e.Id == "999");
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task SearchAsync_IsQuietWhenTheCatalogueWillNotAnswer(HttpStatusCode status)
        {
            Assert.Empty(await ServiceReturning("{}", status).SearchAsync("anything", null));
        }

        [Fact]
        public async Task SearchAsync_IsQuietOnNonsense()
        {
            Assert.Empty(await ServiceReturning("not json at all").SearchAsync("anything", null));
            Assert.Empty(await ServiceReturning("""{"items":[]}""").SearchAsync("anything", null));
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "  ")]
        public async Task SearchAsync_AsksNothingWithoutATitleOrAuthor(string? title, string? author)
        {
            var handler = new StubHandler("{}", HttpStatusCode.OK);
            var client = new HttpClient(handler) { BaseAddress = new Uri("https://thunder.api.overdrive.com/") };
            var service = new OverDriveService(client, NullLogger<OverDriveService>.Instance);

            Assert.Empty(await service.SearchAsync(title, author));
            Assert.Equal(0, handler.Calls);
        }

        private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
        {
            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
