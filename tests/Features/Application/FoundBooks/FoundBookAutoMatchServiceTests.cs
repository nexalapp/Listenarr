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
using Listenarr.Application.Audiobooks.Transcription;
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.FoundBooks;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.FoundBooks
{
    /// <summary>
    /// The scan's own identification: tags first, then one listen for a row whose tags
    /// led nowhere, and the answer written to the row. Never a second listen, never a
    /// near answer over a match already chosen.
    /// </summary>
    [Trait("Name", "FoundBookAutoMatchServiceTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookAutoMatchServiceTests : BaseTests
    {
        private readonly Mock<IFoundBookCatalogueMatcher> _matcher = new();
        private readonly Mock<IFoundBookMatchService> _matches = new();
        private IFoundBookRepository _repository = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _repository = _provider.GetRequiredService<IFoundBookRepository>();
            _matches
                .Setup(m => m.SetMatchAsync(It.IsAny<int>(), It.IsAny<FoundBookMatchChoice?>(), It.IsAny<CancellationToken>()))
                .Returns<int, FoundBookMatchChoice?, CancellationToken>(async (id, choice, ct) =>
                {
                    await _repository.UpdateAsync(id, r =>
                    {
                        r.MatchAsin = choice?.Asin;
                        r.MatchTitle = choice?.Title;
                        r.MatchConfidence = choice?.Confidence;
                    }, ct);
                    return await _repository.GetAsync(id, ct);
                });
        }

        private async Task<FoundBook> GivenRow(string title = "5da8cead8779ae4719256549", double? matchConfidence = null, string? matchAsin = null) =>
            await _repository.AddAsync(new FoundBook
            {
                ClusterKey = Guid.NewGuid().ToString("N"),
                Signature = "sig",
                WatchFolder = "/downloads",
                BookFolder = "/downloads/pack",
                FilesJson = FoundBookFilesJson.Serialize([new FoundBookFileEntry("/downloads/pack/01.mp3", 1, DateTime.UtcNow, true, null)]),
                AudioFileCount = 1,
                DetectedTitle = title,
                Completeness = FoundBookCompleteness.Complete,
                LibraryStatus = FoundBookLibraryStatus.New,
                State = FoundBookState.Pending,
                MatchAsin = matchAsin,
                MatchConfidence = matchConfidence
            });

        private static FoundBookCatalogueMatch Match(string asin, bool high) =>
            new(new AudibleBookMetadata { Asin = asin, Title = "Fearless", Authors = ["Jack Campbell"] }, high, high ? "exact" : "nearest");

        private void GivenHeard(int id, string? title, string? author) =>
            _matches
                .Setup(m => m.ListenAsync(id, It.IsAny<CancellationToken>()))
                .Returns<int, CancellationToken>(async (rowId, ct) =>
                {
                    await _repository.UpdateAsync(rowId, r =>
                    {
                        r.HeardTitle = title;
                        r.HeardAuthor = author;
                        r.HeardAt = DateTime.UtcNow;
                    }, ct);
                    return new FoundBookListenResult(new AudioCredits(title, author, null), $"{title}, by {author}");
                });

        private FoundBookAutoMatchService BuildService() => new(
            _repository,
            _matcher.Object,
            _matches.Object,
            NullLogger<FoundBookAutoMatchService>.Instance);

        [Fact]
        public async Task SureMatchFromTags_IsWrittenWithoutListening()
        {
            var row = await GivenRow(title: "Fearless");
            _matcher.Setup(m => m.MatchAsync(It.IsAny<FoundBook>(), It.IsAny<CancellationToken>())).ReturnsAsync(Match("B00ABCDEF1", high: true));

            var summary = await BuildService().RunAsync();

            Assert.Equal(1, summary.Matched);
            Assert.Equal(0, summary.Listened);
            var after = (await _repository.GetAsync(row.Id))!;
            Assert.Equal("B00ABCDEF1", after.MatchAsin);
            Assert.Equal(FoundBookAutoMatchService.SureConfidence, after.MatchConfidence);
            _matches.Verify(m => m.ListenAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task NoMatchFromTags_ListensOnceAndAsksAgainWithTheCredits()
        {
            var row = await GivenRow();
            _matcher
                .Setup(m => m.MatchAsync(It.Is<FoundBook>(r => r.HeardTitle == null), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FoundBookCatalogueMatch?)null);
            _matcher
                .Setup(m => m.MatchAsync(It.Is<FoundBook>(r => r.HeardTitle == "Fearless"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Match("B00ABCDEF1", high: true));
            GivenHeard(row.Id, "Fearless", "Jack Campbell");

            var summary = await BuildService().RunAsync();

            Assert.Equal(1, summary.Listened);
            Assert.Equal(1, summary.Matched);
            Assert.Equal("B00ABCDEF1", (await _repository.GetAsync(row.Id))!.MatchAsin);

            // The next scan: still no sure match from the tags, but the row was heard.
            _matcher
                .Setup(m => m.MatchAsync(It.Is<FoundBook>(r => r.HeardTitle == "Fearless"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Match("B00ABCDEF1", high: false));
            await _repository.UpdateAsync(row.Id, r => r.MatchConfidence = 0.5);
            await BuildService().RunAsync();
            _matches.Verify(m => m.ListenAsync(row.Id, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task NearMatch_DoesNotReplaceAMatchAlreadyOnTheRow()
        {
            var row = await GivenRow(matchAsin: "B00CHOSEN01", matchConfidence: 0.7);
            _matcher.Setup(m => m.MatchAsync(It.IsAny<FoundBook>(), It.IsAny<CancellationToken>())).ReturnsAsync(Match("B00NEAREST1", high: false));
            GivenHeard(row.Id, null, null);

            await BuildService().RunAsync();

            Assert.Equal("B00CHOSEN01", (await _repository.GetAsync(row.Id))!.MatchAsin);
        }

        [Fact]
        public async Task SureMatchAlreadyOnTheRow_IsLeftAlone()
        {
            await GivenRow(matchAsin: "B00CHOSEN01", matchConfidence: 1.0);

            var summary = await BuildService().RunAsync();

            Assert.Equal(0, summary.Considered);
            _matcher.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ModelStillDownloading_LeavesTheRowToBeHeardNextScan()
        {
            var row = await GivenRow();
            _matcher.Setup(m => m.MatchAsync(It.IsAny<FoundBook>(), It.IsAny<CancellationToken>())).ReturnsAsync((FoundBookCatalogueMatch?)null);
            _matches
                .Setup(m => m.ListenAsync(row.Id, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TranscriptionUnavailableException("downloading"));

            var summary = await BuildService().RunAsync();

            Assert.Equal(0, summary.Matched);
            Assert.Contains(summary.Notes, note => note.Contains("downloading"));
            Assert.Null((await _repository.GetAsync(row.Id))!.HeardAt);
        }
    }
}
