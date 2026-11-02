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

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Submission
{
    /// <summary>
    /// Every grab passes through SendToDownloadClientAsync, which is why the blocklist has
    /// to be enforced there rather than only in the automatic search's ranking.
    ///
    /// Measured on this library on 2026-11-02: one release whose par2 set is 420 blocks
    /// short, and so can never be repaired, had been grabbed nine times in two days. Its
    /// block was in the table the whole time, matching on fingerprint and size - the
    /// callers doing the grabbing simply never looked. 151 failed rows were 35 books.
    /// </summary>
    [Trait("Name", "SendToDownloadClientBlocklistTests")]
    [Trait("Category", "Downloads")]
    public sealed class SendToDownloadClientBlocklistTests : BaseTests
    {
        private const int Book = 4242;
        private const string ReleaseTitle = "Terry Pratchett's 'Discworld', Bk 22 - The Last Continent (NMR 64 kbps)";
        private const long ReleaseSize = 387_973_120;

        private IDownloadService Downloads => _provider.GetRequiredService<IDownloadService>();

        private async Task BlockTheReleaseAsync()
        {
            await _provider.GetRequiredService<IReleaseBlocklistRepository>().AddAsync(new BlockedRelease
            {
                AudiobookId = Book,
                ReleaseTitle = ReleaseTitle,
                Fingerprint = BlockedReleaseMatch.Fingerprint(ReleaseTitle),
                SizeBytes = ReleaseSize,
                Reason = DownloadFailureReason.Damaged,
                Detail = "Repair failed: not enough par-blocks"
            });
        }

        private static SearchResult TheRelease(string? title = null, long? size = null) => new SearchResult
        {
            Id = string.Empty,
            Title = title ?? ReleaseTitle,
            Size = size ?? ReleaseSize,
            NzbUrl = "https://indexer.example/getnzb/abc",
            DownloadType = "Usenet",
            Source = "NZBgeek (Prowlarr)"
        };

        /// <summary>
        /// The refusal happens before any client is asked, so it needs no download client
        /// configured - which is also why it cannot be bypassed by a caller that skips the
        /// search.
        /// </summary>
        [Fact]
        public async Task ABlockedReleaseIsRefusedWhicheverCallerSendsIt()
        {
            await BlockTheReleaseAsync();

            var downloadId = await Downloads.SendToDownloadClientAsync(TheRelease(), null, Book);

            Assert.Equal(string.Empty, downloadId);
        }

        /// <summary>
        /// Sizes for one posting differ a little between indexers, and a block has to
        /// survive being offered the same release by another one.
        /// </summary>
        [Fact]
        public async Task TheSameReleaseAtASlightlyDifferentSizeIsStillRefused()
        {
            await BlockTheReleaseAsync();

            var downloadId = await Downloads.SendToDownloadClientAsync(
                TheRelease(size: (long)(ReleaseSize * 1.04)), null, Book);

            Assert.Equal(string.Empty, downloadId);
        }

        /// <summary>
        /// A block belongs to one book. Another book offered the same release has not tried
        /// it, and must not inherit someone else's failure.
        /// </summary>
        [Fact]
        public async Task AnotherBookIsNotRefusedTheSameRelease()
        {
            await BlockTheReleaseAsync();

            // No client is configured, so this cannot complete - but it must get past the
            // blocklist to fail for that reason instead.
            await Assert.ThrowsAnyAsync<Exception>(() =>
                Downloads.SendToDownloadClientAsync(TheRelease(), null, Book + 1));
        }

        /// <summary>
        /// A grab with no book behind it has no blocklist to consult, and must not be
        /// stopped by one.
        /// </summary>
        [Fact]
        public async Task AGrabWithNoBookIsNotRefused()
        {
            await BlockTheReleaseAsync();

            await Assert.ThrowsAnyAsync<Exception>(() =>
                Downloads.SendToDownloadClientAsync(TheRelease(), null, null));
        }

        /// <summary>
        /// The override is for a person who means it: they picked a release that failed
        /// before and want it anyway. It must reach the client rather than be refused.
        /// </summary>
        [Fact]
        public async Task TheOverrideSendsABlockedReleaseAnyway()
        {
            await BlockTheReleaseAsync();

            await Assert.ThrowsAnyAsync<Exception>(() =>
                Downloads.SendToDownloadClientAsync(TheRelease(), null, Book, allowBlockedRelease: true));
        }

        /// <summary>A release nobody blocked is never refused by this check.</summary>
        [Fact]
        public async Task AReleaseNobodyBlockedIsNotRefused()
        {
            await BlockTheReleaseAsync();

            await Assert.ThrowsAnyAsync<Exception>(() =>
                Downloads.SendToDownloadClientAsync(
                    TheRelease(title: "Terry Pratchett - The Last Continent (2005) [M4B 128k]"),
                    null,
                    Book));
        }
    }
}
