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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Configuration
{
    /// <summary>
    /// The books page's custom filters are stored server-side so the same library shows
    /// the same filters on every machine; localStorage is per-browser, which is why a
    /// laptop and a desktop disagreed about which filters existed.
    /// </summary>
    [Trait("Name", "LibraryCustomFiltersTests")]
    [Trait("Category", "Api")]
    public sealed class LibraryCustomFiltersTests : BaseTests
    {
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly ApplicationSettings _settings = new();

        private SettingsController BuildController()
        {
            _configuration
                .Setup(service => service.GetApplicationSettingsAsync())
                .ReturnsAsync(_settings);
            _configuration
                .Setup(service => service.SaveApplicationSettingsAsync(It.IsAny<ApplicationSettings>()))
                .Returns(Task.CompletedTask);

            return new SettingsController(
                _configuration.Object,
                NullLogger<SettingsController>.Instance,
                new Mock<IHubBroadcaster>().Object);
        }

        private static JsonElement Body(string json) =>
            JsonDocument.Parse(json).RootElement;

        [Fact]
        public void ApplicationSettings_StartWithAnEmptyFilterArray()
        {
            // Not an empty string: the page parses this straight back, and "" is not
            // something it can read.
            Assert.Equal("[]", new ApplicationSettings().LibraryCustomFiltersJson);
        }

        [Fact]
        public async Task SaveLibraryCustomFilters_StoresTheArrayVerbatim()
        {
            // The rule grammar belongs to the filter editor; the server only has to hand
            // back what it was given, so it must not reshape the payload.
            const string filters =
                """[{"id":"cf-1","label":"Missing narration","rules":[{"field":"narrators","operator":"isEmpty","value":""}]}]""";

            var action = await BuildController().SaveLibraryCustomFilters(Body(filters));

            Assert.IsType<OkObjectResult>(action);
            Assert.Equal(filters, _settings.LibraryCustomFiltersJson);
            _configuration.Verify(
                service => service.SaveApplicationSettingsAsync(_settings),
                Times.Once);
        }

        [Fact]
        public async Task SaveLibraryCustomFilters_AcceptsAnEmptyArrayAsDeletingTheLast()
        {
            _settings.LibraryCustomFiltersJson = """[{"id":"cf-1","label":"Mine","rules":[]}]""";

            await BuildController().SaveLibraryCustomFilters(Body("[]"));

            Assert.Equal("[]", _settings.LibraryCustomFiltersJson);
        }

        [Theory]
        [InlineData("{\"not\":\"an array\"}")]
        [InlineData("\"a string\"")]
        [InlineData("42")]
        [InlineData("null")]
        public async Task SaveLibraryCustomFilters_RefusesAnythingButAnArray(string body)
        {
            // Anything else comes back as filters the page cannot read, so it is worth
            // refusing at the door rather than storing.
            var action = await BuildController().SaveLibraryCustomFilters(Body(body));

            Assert.IsType<BadRequestObjectResult>(action);
            Assert.Equal("[]", _settings.LibraryCustomFiltersJson);
            _configuration.Verify(
                service => service.SaveApplicationSettingsAsync(It.IsAny<ApplicationSettings>()),
                Times.Never);
        }

        [Fact]
        public async Task SaveLibraryCustomFilters_LeavesEverythingElseInSettingsAlone()
        {
            // Its own endpoint precisely so a filter does not travel with the whole
            // settings object - which carries a concurrency version, and would make
            // creating a filter conflict with unrelated configuration.
            _settings.OutputPath = "/audiobooks";
            _settings.Version = 7;

            await BuildController().SaveLibraryCustomFilters(Body("""[{"id":"cf-1"}]"""));

            Assert.Equal("/audiobooks", _settings.OutputPath);
            Assert.Equal(7, _settings.Version);
        }
    }
}
