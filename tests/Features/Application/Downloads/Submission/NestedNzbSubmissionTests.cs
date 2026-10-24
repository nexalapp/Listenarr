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

using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Submission
{
    /// <summary>
    /// A finished download that holds only an NZB of the release (Dungeon Crawler Carl 2–6
    /// and Gauntlet Wars, grabbed from abook.link) is submitted as the download it points to.
    /// </summary>
    [Trait("Name", "NestedNzbSubmissionTests")]
    [Trait("Category", "Downloads")]
    public sealed class NestedNzbSubmissionTests : BaseTests
    {
        private sealed class MarkingProtector : ISecretProtector
        {
            public string Protect(string plaintext) => "protected:" + plaintext;
            public string Unprotect(string protectedValue) => protectedValue["protected:".Length..];
        }

        [Theory]
        [InlineData(new[] { "/d/5oI7a7D5A0c1ZL5cGdzB.nzb" }, true)]
        [InlineData(new[] { "/d/x.NZB" }, true)]
        [InlineData(new[] { "/d/x.nzb", "/d/x.mp3" }, false)]
        [InlineData(new[] { "/d/x.m4b" }, false)]
        [InlineData(new string[0], false)]
        public void OnlyADownloadOfNothingButNzbFilesIsOne(string[] files, bool expected)
        {
            Assert.Equal(expected, DownloadProcessingJobProcessor.IsOnlyNzbFiles(files));
        }

        [Fact]
        public async Task TheNzbGoesToTheSameClientAndBookWithTheOuterGrabsPassword()
        {
            PreparedDownloadSubmission? sent = null;
            var gateway = new Mock<IDownloadClientGateway>();
            gateway
                .Setup(g => g.AddAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<PreparedDownloadSubmission>(), It.IsAny<CancellationToken>()))
                .Callback<DownloadClientConfiguration, PreparedDownloadSubmission, CancellationToken>((_, submission, _) => sent = submission)
                .ReturnsAsync(new DownloadClientSubmissionResult("37800"));
            _services.AddSingleton(gateway.Object);
            _services.AddSingleton<ISecretProtector>(new MarkingProtector());
            Init();

            var client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfiguration
            {
                Id = "nzbget-1",
                Name = "NZBGet",
                Type = "nzbget",
                Host = "localhost",
                Port = 6789,
                IsEnabled = true
            });
            var audiobook = await CreateAudiobook();
            var outer = new DownloadBuilder().Build();
            outer.DownloadClientId = client.Id;
            outer.AudiobookId = audiobook.Id;
            outer.Title = "Matt Dinniman - Dungeon Crawler Carl 02 - Carl's Doomsday Scenario (2021) [28 MP3s]";
            outer.Status = DownloadStatus.Moved;
            outer.Metadata ??= new Dictionary<string, object>();
            outer.Metadata["Source"] = "Abook.link";
            outer.Metadata[DownloadMetadataKeys.ProtectedArchivePassword] = "protected:the-post-password";
            await _downloadRepository.AddAsync(outer);

            var nzb = "<nzb><file subject=\"5oI7a7D5A0c1ZL5cGdzB.part01.rar\"/></nzb>"u8.ToArray();
            var downloadId = await _provider.GetRequiredService<DownloadService>()
                .SendNestedNzbAsync(outer, "5oI7a7D5A0c1ZL5cGdzB.nzb", nzb);

            var usenet = Assert.IsType<PreparedUsenetSubmission>(sent);
            Assert.Equal("the-post-password", usenet.Password);
            Assert.Equal(nzb, usenet.NzbBytes);
            Assert.Equal("5oI7a7D5A0c1ZL5cGdzB.nzb", usenet.FileName);

            var continuation = await _downloadRepository.GetByIdAsync(downloadId);
            Assert.NotNull(continuation);
            Assert.Equal(audiobook.Id, continuation!.AudiobookId);
            Assert.Equal(client.Id, continuation.DownloadClientId);
            Assert.Equal(outer.Id, continuation.GetMetadataString(DownloadMetadataKeys.ContinuesFrom));
            Assert.Equal("37800", continuation.GetMetadataString("ClientDownloadId"));
            Assert.Equal("protected:the-post-password",
                continuation.GetMetadataString(DownloadMetadataKeys.ProtectedArchivePassword));
        }
    }
}
