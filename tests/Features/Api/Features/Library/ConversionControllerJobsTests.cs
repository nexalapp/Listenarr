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
using Listenarr.Application.Audiobooks.Conversion;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    [Trait("Name", "ConversionControllerJobsTests")]
    [Trait("Category", "Api")]
    public sealed class ConversionControllerJobsTests : BaseTests
    {
        [Fact]
        public async Task GetJobs_SaysWhichKindOfJobEachIs()
        {
            // The Activity page loads its rows from this list; without the kind every
            // repair read back as a conversion and was labelled "Convert to M4B".
            var queue = new Mock<IConversionQueueService>();
            queue
                .Setup(q => q.GetVisibleJobsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new ConversionJob { AudiobookId = 1, Kind = ConversionJobKind.Repair, SourceFileCount = 3, CompletedSourceFiles = 2 },
                    new ConversionJob { AudiobookId = 2, Kind = ConversionJobKind.Convert }
                ]);

            var action = await new ConversionController(queue.Object, NullLogger<ConversionController>.Instance)
                .GetJobs();

            var rows = Assert.IsAssignableFrom<IEnumerable<object>>(Assert.IsType<OkObjectResult>(action).Value)
                .Select(row => row.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(row)))
                .ToList();
            Assert.Equal("Repair", rows[0]["kind"]);
            Assert.Equal(2, rows[0]["completedSourceFiles"]);
            Assert.Equal("Convert", rows[1]["kind"]);
        }
    }
}
