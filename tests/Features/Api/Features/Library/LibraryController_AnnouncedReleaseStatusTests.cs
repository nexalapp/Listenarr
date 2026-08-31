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
using Microsoft.AspNetCore.Mvc;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    [Trait("Area", "LibraryApi")]
    [Trait("Name", "LibraryController_AnnouncedReleaseStatusTests")]
    [Trait("Category", "LibraryController")]
    public class LibraryController_AnnouncedReleaseStatusTests : BaseTests
    {
        [Fact]
        [Trait("Method", "GetAll")]
        [Trait("Scenario", "DistinguishesAnnouncedFromWantedAndOwned")]
        public async Task GetAll_TellsAnnouncedApartFromWantedAndOwned()
        {
            // Given three books a monitored series would produce: one already on disk, one
            // released but missing, and one Audible has only announced.
            var owned = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("This Inevitable Ruin")
                .WithAuthor("Matt Dinniman")
                .WithMonitored()
                .WithPublishedDate(new DateOnly(2025, 2, 11))
                .WithBasePath(FileUtils.GetAbsolutePath("library", "This Inevitable Ruin"))
                .WithFilePath(FileUtils.GetAbsolutePath("library", "This Inevitable Ruin", "book.m4b"))
                .Build());

            await _audiobookFileRepository.AddAsync(new AudiobookFileBuilder()
                .WithAudiobook(owned)
                .WithPath(owned.FilePath!)
                .WithFormat("m4b")
                .Build());

            var wanted = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("The Butcher's Masquerade")
                .WithAuthor("Matt Dinniman")
                .WithMonitored()
                .WithPublishedDate(new DateOnly(2022, 5, 26))
                .Build());

            var announced = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Once a Crown")
                .WithAuthor("Sarah Arthur")
                .WithMonitored()
                .WithPublishedDate(DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1))
                .Build());

            var controller = _provider.GetRequiredService<LibraryController>();

            // When
            var actionResult = await controller.GetAll();

            // Then each lands in its own status
            var ok = Assert.IsType<OkObjectResult>(actionResult);
            var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var doc = JsonDocument.Parse(json);

            JsonElement Item(int id) => doc.RootElement
                .EnumerateArray()
                .Single(element => element.GetProperty("id").GetInt32() == id);

            Assert.Equal("quality-match", Item(owned.Id).GetProperty("status").GetString());
            Assert.Equal("no-file", Item(wanted.Id).GetProperty("status").GetString());
            Assert.Equal("announced", Item(announced.Id).GetProperty("status").GetString());
        }

        [Fact]
        [Trait("Method", "GetAll")]
        [Trait("Scenario", "AnnouncedRetainsItsDateAndWantedFlag")]
        public async Task GetAll_KeepsTheAnnouncedDate_AndLeavesWantedAlone()
        {
            // Given an announced book with only a month named
            var announcedMonth = $"{DateTime.UtcNow.Year + 2:D4}-04";
            var book = new AudiobookBuilder()
                .WithTitle("Announced Only By Month")
                .WithAuthor("Sarah Arthur")
                .WithMonitored()
                .Build();
            book.PublishedDate = announcedMonth;
            book = await _audiobookRepository.AddAsync(book);

            var controller = _provider.GetRequiredService<LibraryController>();

            // When
            var actionResult = await controller.GetAll();

            // Then the imprecise date reaches the client untouched, so the client can render
            // it as a month rather than inventing a day for it.
            var ok = Assert.IsType<OkObjectResult>(actionResult);
            var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var doc = JsonDocument.Parse(json);
            var item = doc.RootElement
                .EnumerateArray()
                .Single(element => element.GetProperty("id").GetInt32() == book.Id);

            Assert.Equal("announced", item.GetProperty("status").GetString());
            Assert.Equal(announcedMonth, item.GetProperty("publishedDate").GetString());

            // Wanted stays a separate axis: it still means "monitored with nothing on disk",
            // and it is the status that says the book cannot be had yet.
            Assert.True(item.GetProperty("wanted").GetBoolean());
        }
    }
}
