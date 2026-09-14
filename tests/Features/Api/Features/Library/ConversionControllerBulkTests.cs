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
    [Trait("Name", "ConversionControllerBulkTests")]
    [Trait("Category", "Api")]
    public sealed class ConversionControllerBulkTests : BaseTests
    {
        private readonly Mock<IConversionQueueService> _queue = new();

        private ConversionController BuildController() => new(
            _queue.Object,
            NullLogger<ConversionController>.Instance);

        private void GivenOutcome(int audiobookId, ConversionEnqueueResult result) =>
            _queue
                .Setup(queue => queue.EnqueueAsync(
                    audiobookId,
                    ConversionTrigger.Manual,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);

        private static IDictionary<string, object?> Payload(IActionResult action)
        {
            var ok = Assert.IsType<OkObjectResult>(action);
            Assert.NotNull(ok.Value);
            return ok.Value!
                .GetType()
                .GetProperties()
                .ToDictionary(property => property.Name, property => property.GetValue(ok.Value));
        }

        private static IReadOnlyList<IDictionary<string, object?>> Results(IActionResult action)
        {
            var rows = Assert.IsAssignableFrom<IEnumerable<object>>(Payload(action)["results"]);
            return rows
                .Select(row => (IDictionary<string, object?>)row
                    .GetType()
                    .GetProperties()
                    .ToDictionary(property => property.Name, property => property.GetValue(row)))
                .ToList();
        }

        [Fact]
        public async Task ConvertBulk_ReportsEveryBookEvenWhenOnlySomeCouldBeQueued()
        {
            // The ordinary case: a selection made from a list holds books that cannot
            // convert, and the caller has to be told which were which.
            GivenOutcome(1, new ConversionEnqueueResult(
                ConversionEnqueueOutcome.Queued, Guid.NewGuid()));
            GivenOutcome(2, new ConversionEnqueueResult(
                ConversionEnqueueOutcome.NothingToConvert,
                Reason: "This book has no MP3 files to convert."));
            GivenOutcome(3, new ConversionEnqueueResult(
                ConversionEnqueueOutcome.AlreadyQueued,
                Guid.NewGuid(),
                "This book is already queued for conversion."));

            var action = await BuildController().ConvertBulk(
                new BulkConvertRequest { AudiobookIds = [1, 2, 3] });

            var payload = Payload(action);
            Assert.Equal(3, payload["requestedCount"]);
            Assert.Equal(1, payload["queuedCount"]);

            var results = Results(action);
            Assert.Equal(3, results.Count);
            Assert.Equal("Queued", results[0]["outcome"]);
            Assert.Equal("NothingToConvert", results[1]["outcome"]);
            Assert.Equal("This book has no MP3 files to convert.", results[1]["reason"]);
            Assert.Equal("AlreadyQueued", results[2]["outcome"]);
        }

        [Fact]
        public async Task ConvertBulk_IsStillOkWhenNothingCouldBeQueued()
        {
            // Not an error: the books were considered and each has a reason. A failure
            // status would make the caller discard the reasons it needs to show.
            GivenOutcome(1, new ConversionEnqueueResult(
                ConversionEnqueueOutcome.EncoderUnavailable,
                Reason: "No ffmpeg encoder is installed, so conversion is unavailable."));

            var action = await BuildController().ConvertBulk(
                new BulkConvertRequest { AudiobookIds = [1] });

            Assert.Equal(0, Payload(action)["queuedCount"]);
            Assert.Equal("EncoderUnavailable", Results(action)[0]["outcome"]);
        }

        [Fact]
        public async Task ConvertBulk_QueuesARepeatedIdOnce()
        {
            GivenOutcome(7, new ConversionEnqueueResult(
                ConversionEnqueueOutcome.Queued, Guid.NewGuid()));

            var action = await BuildController().ConvertBulk(
                new BulkConvertRequest { AudiobookIds = [7, 7, 7] });

            Assert.Equal(1, Payload(action)["requestedCount"]);
            _queue.Verify(
                queue => queue.EnqueueAsync(7, ConversionTrigger.Manual, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ConvertBulk_RequestsTheManualTriggerSoTheAutomaticSettingDoesNotGateIt()
        {
            // Asking for these books is an explicit instruction, exactly as the
            // single-book request is. Going through as Automatic would refuse the whole
            // selection whenever the setting is off, which is its default.
            GivenOutcome(4, new ConversionEnqueueResult(
                ConversionEnqueueOutcome.Queued, Guid.NewGuid()));

            await BuildController().ConvertBulk(new BulkConvertRequest { AudiobookIds = [4] });

            _queue.Verify(
                queue => queue.EnqueueAsync(4, ConversionTrigger.Manual, It.IsAny<CancellationToken>()),
                Times.Once);
            _queue.Verify(
                queue => queue.EnqueueAsync(
                    It.IsAny<int>(),
                    ConversionTrigger.Automatic,
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ConvertBulk_RefusesAnEmptySelectionRatherThanReportingNothing()
        {
            var action = await BuildController().ConvertBulk(new BulkConvertRequest());

            Assert.IsType<BadRequestObjectResult>(action);
            _queue.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ConvertBulk_StopsWhenTheCallerGivesUp()
        {
            // A whole library is queued one book at a time, so an abandoned request must
            // not keep writing rows nobody is waiting for.
            using var cancellation = new CancellationTokenSource();
            GivenOutcome(1, new ConversionEnqueueResult(
                ConversionEnqueueOutcome.Queued, Guid.NewGuid()));
            _queue
                .Setup(queue => queue.EnqueueAsync(
                    1,
                    ConversionTrigger.Manual,
                    It.IsAny<CancellationToken>()))
                .Callback(cancellation.Cancel)
                .ReturnsAsync(new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.Queued, Guid.NewGuid()));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                BuildController().ConvertBulk(
                    new BulkConvertRequest { AudiobookIds = [1, 2] },
                    cancellation.Token));

            _queue.Verify(
                queue => queue.EnqueueAsync(2, ConversionTrigger.Manual, It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
