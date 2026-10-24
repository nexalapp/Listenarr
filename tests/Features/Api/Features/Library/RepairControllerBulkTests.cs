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
using Listenarr.Application.Audiobooks.Repair;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    [Trait("Name", "RepairControllerBulkTests")]
    [Trait("Category", "Api")]
    public sealed class RepairControllerBulkTests : BaseTests
    {
        private readonly Mock<IConversionQueueService> _queue = new();

        private RepairController BuildController() => new(
            _queue.Object,
            Mock.Of<IRepairPreviewService>(),
            Mock.Of<IAudioEnhancer>(),
            NullLogger<RepairController>.Instance);

        private void GivenOutcome(int audiobookId, ConversionEnqueueResult result) =>
            _queue
                .Setup(queue => queue.EnqueueRepairAsync(audiobookId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);

        private static IDictionary<string, object?> Properties(object value) =>
            value.GetType()
                .GetProperties()
                .ToDictionary(property => property.Name, property => property.GetValue(value));

        private static IDictionary<string, object?> Payload(IActionResult action)
        {
            var ok = Assert.IsType<OkObjectResult>(action);
            Assert.NotNull(ok.Value);
            return Properties(ok.Value!);
        }

        private static IReadOnlyList<IDictionary<string, object?>> Results(IActionResult action) =>
            Assert.IsAssignableFrom<IEnumerable<object>>(Payload(action)["results"])
                .Select(Properties)
                .ToList();

        [Fact]
        public async Task RepairBulk_QueuesRepairsNotConversions_AndReportsEveryBook()
        {
            GivenOutcome(1, new ConversionEnqueueResult(ConversionEnqueueOutcome.Queued, Guid.NewGuid()));
            GivenOutcome(2, new ConversionEnqueueResult(
                ConversionEnqueueOutcome.AlreadyQueued,
                Guid.NewGuid(),
                "This book is already queued."));

            var action = await BuildController().RepairBulk(new BulkConvertRequest { AudiobookIds = [1, 2] });

            var payload = Payload(action);
            Assert.Equal(2, payload["requestedCount"]);
            Assert.Equal(1, payload["queuedCount"]);
            var results = Results(action);
            Assert.Equal("Queued", results[0]["outcome"]);
            Assert.Equal("AlreadyQueued", results[1]["outcome"]);
            _queue.Verify(
                queue => queue.EnqueueAsync(
                    It.IsAny<int>(),
                    It.IsAny<ConversionTrigger>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task RepairBulk_IsStillOkWhenTheModelIsNotReady()
        {
            // Per-book reasons are what the caller shows; a 503 would discard them.
            GivenOutcome(1, new ConversionEnqueueResult(
                ConversionEnqueueOutcome.EnhancerUnavailable,
                Reason: "The enhancement model is not installed."));

            var action = await BuildController().RepairBulk(new BulkConvertRequest { AudiobookIds = [1] });

            Assert.Equal(0, Payload(action)["queuedCount"]);
            Assert.Equal("EnhancerUnavailable", Results(action)[0]["outcome"]);
        }

        [Fact]
        public async Task RepairBulk_QueuesARepeatedIdOnce()
        {
            GivenOutcome(7, new ConversionEnqueueResult(ConversionEnqueueOutcome.Queued, Guid.NewGuid()));

            var action = await BuildController().RepairBulk(new BulkConvertRequest { AudiobookIds = [7, 7] });

            Assert.Equal(1, Payload(action)["requestedCount"]);
            _queue.Verify(queue => queue.EnqueueRepairAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RepairBulk_RefusesAnEmptySelection()
        {
            var action = await BuildController().RepairBulk(new BulkConvertRequest());

            Assert.IsType<BadRequestObjectResult>(action);
            _queue.VerifyNoOtherCalls();
        }
    }
}
