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
using Listenarr.Application.Common.Exceptions;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Downloads
{
    /// <summary>
    /// A grab that cannot happen has to say why, in a status that keeps the reason.
    ///
    /// This covers a live report: clicking grab spun and then stopped with nothing
    /// useful. The server knew exactly what was wrong - no NZB client was enabled - and
    /// said so, but it threw a bare Exception, so the controller's catch-all returned
    /// 500 and ServerErrorProblemDetailsFilter replaced the body with "Internal server
    /// error" and a null detail. The advice was written and then discarded in transit.
    /// </summary>
    [Trait("Name", "DownloadControllerErrorReportingTests")]
    [Trait("Category", "Api")]
    public sealed class DownloadControllerErrorReportingTests : BaseTests
    {
        private readonly Mock<IDownloadService> _downloadService = new();
        private readonly Mock<IDownloadReferenceService> _references = new();

        private const string Advice =
            "No NZB download client is enabled, so this release cannot be sent anywhere. "
            + "Add and enable SABnzbd or NZBGet under Settings > Download Clients, then try again.";

        private DownloadController BuildController()
        {
            _references
                .Setup(service => service.Read(It.IsAny<string>()))
                .Returns(new TrustedDownloadCandidate(
                    Id: "r1",
                    Title: "Leave the World Behind",
                    Artist: "Rumaan Alam",
                    Album: "Leave the World Behind",
                    Source: "Abook.link",
                    Quality: null,
                    Language: null,
                    Size: 0,
                    Seeders: null,
                    SourceDescriptor: new DownloadSourceDescriptor(
                        IndexerId: null,
                        IndexerImplementation: null,
                        Protocol: DownloadProtocol.Usenet,
                        Locators: [])));

            return new DownloadController(
                _downloadService.Object,
                new Mock<IDownloadQueueService>().Object,
                new Mock<IDownloadProcessingJobService>().Object,
                NullLogger<DownloadController>.Instance,
                _references.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
        }

        [Fact]
        public async Task SendToDownloadClient_ReportsAMisconfiguredClientAsAConflictThatKeepsItsAdvice()
        {
            _downloadService
                .Setup(service => service.SendToDownloadClientAsync(
                    It.IsAny<TrustedDownloadCandidate>(),
                    It.IsAny<string?>(),
                    It.IsAny<int?>()))
                .ThrowsAsync(new ApplicationConflictException("download_client_unavailable", Advice));

            var action = await BuildController().SendToDownloadClient(
                new SendDownloadRequest { DownloadReference = "ref-1" });

            var conflict = Assert.IsType<ConflictObjectResult>(action.Result);
            var problem = Assert.IsType<ProblemDetails>(conflict.Value);

            // 4xx is the point: the filter only rewrites 5xx, so this detail survives.
            Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
            Assert.Equal(Advice, problem.Detail);
            Assert.Equal("download_client_unavailable", problem.Extensions["code"]);
        }

        [Fact]
        public async Task SendToDownloadClient_NamesTheProtocolItCouldNotSend()
        {
            // A usenet grab once reported "failed to send torrent", which sent the
            // diagnosis in the wrong direction.
            _downloadService
                .Setup(service => service.SendToDownloadClientAsync(
                    It.IsAny<TrustedDownloadCandidate>(),
                    It.IsAny<string?>(),
                    It.IsAny<int?>()))
                .ThrowsAsync(new Listenarr.Application.Common.DownloadClientSubmissionException(
                    "The client refused it."));

            var action = await BuildController().SendToDownloadClient(
                new SendDownloadRequest { DownloadReference = "ref-1" });

            var result = Assert.IsType<ObjectResult>(action.Result);
            var problem = Assert.IsType<ProblemDetails>(result.Value);

            Assert.Equal(StatusCodes.Status502BadGateway, problem.Status);
            Assert.Contains("usenet", problem.Title, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("The client refused it.", problem.Detail);
        }
    }
}
