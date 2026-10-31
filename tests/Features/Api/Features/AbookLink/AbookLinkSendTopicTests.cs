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

using Listenarr.Api.Features.AbookLink;
using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.AbookLink
{
    /// <summary>
    /// Adding a book Audible has no record of from an abook.link post, then downloading
    /// that post for it.
    /// </summary>
    [Trait("Name", "AbookLinkSendTopicTests")]
    [Trait("Category", "AbookLink")]
    public sealed class AbookLinkSendTopicTests : BaseTests
    {
        private static readonly Indexer Abook = new() { Id = 1, Name = "Abook.link", Implementation = "AbookLink", IsEnabled = true };

        private readonly Mock<IAbookLinkBrowser> _browser = new();
        private readonly Mock<IDownloadService> _downloads = new();
        private readonly Mock<IIndexerRepository> _indexers = new();

        private AbookLinkController Controller() => new(
            _browser.Object,
            Mock.Of<IAbookGrabResolver>(),
            Mock.Of<IAbookGrabDispatcher>(),
            NullLogger<AbookLinkController>.Instance);

        private void Post(AbookParseOutcome outcome)
        {
            var post = new AbookPost
            {
                Outcome = outcome,
                Title = "Solaris: The Definitive Edition",
                Author = "Stanislaw Lem",
                Year = 2011,
                Format = "MP3s"
            };
            _browser.Setup(b => b.GetTopicAsync(71604, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbookBrowseResult(
                    true, 1, [new AbookCandidate(71604, "Stanislaw Lem - Solaris", post)], new AbookParseReport(), null));
        }

        private Task<ActionResult<object>> Send(int audiobookId = 1273) =>
            Controller().SendTopic(71604, new AbookSendTopicRequest { AudiobookId = audiobookId },
                _downloads.Object, _indexers.Object);

        [Fact]
        public async Task ThePostIsSentAsTheBooksDownload()
        {
            _indexers.Setup(i => i.GetEnabledAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync([Abook]);
            Post(AbookParseOutcome.Complete);
            TrustedDownloadCandidate? sent = null;
            _downloads.Setup(d => d.SendToDownloadClientAsync(It.IsAny<TrustedDownloadCandidate>(), null, 1273))
                .Callback<TrustedDownloadCandidate, string?, int?>((candidate, _, _) => sent = candidate)
                .ReturnsAsync("download-1");

            var result = await Send();

            Assert.IsType<OkObjectResult>(result.Result);
            Assert.NotNull(sent);
            // The abook: id is what routes the grab through the abook.link resolver, which
            // thanks the post and resolves its NZB.
            Assert.Equal("abook:71604", sent!.Id);
            Assert.Equal(1, sent.SourceDescriptor.IndexerId);
            Assert.Equal("AbookLink", sent.SourceDescriptor.IndexerImplementation);
            Assert.Equal("Stanislaw Lem - Solaris: The Definitive Edition (2011)", sent.Title);
        }

        [Fact]
        public async Task WithoutAbookLinkConnectedNothingIsSent()
        {
            _indexers.Setup(i => i.GetEnabledAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync([]);

            var result = await Send();

            Assert.IsType<ConflictObjectResult>(result.Result);
            _downloads.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ATopicThatIsNotAReleaseIsRefused()
        {
            _indexers.Setup(i => i.GetEnabledAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync([Abook]);
            Post(AbookParseOutcome.NotARelease);

            var result = await Send();

            Assert.IsType<BadRequestObjectResult>(result.Result);
            _downloads.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AFailedResolutionSaysWhy()
        {
            _indexers.Setup(i => i.GetEnabledAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync([Abook]);
            Post(AbookParseOutcome.Complete);
            _downloads.Setup(d => d.SendToDownloadClientAsync(It.IsAny<TrustedDownloadCandidate>(), null, 1273))
                .ThrowsAsync(new DownloadClientSubmissionException("Resolve NZB: No index holds this release."));

            var result = await Send();

            var error = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, error.StatusCode);
            Assert.Contains("No index holds this release", error.Value!.ToString());
        }
    }
}
