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
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Downloads.Queue;

/// <summary>
/// Which client job a tracked download belongs to, when the client holds several jobs of
/// the same name.
/// </summary>
[Trait("Name", "DownloadQueueMetadataMatcherTests")]
[Trait("Category", "DownloadQueue")]
public sealed class DownloadQueueMetadataMatcherTests : BaseTests
{
    private const string Title = "Stanislaw Lem - The Truth and Other Stories (2022) [23 MP3s]";

    private static readonly DownloadClientConfiguration Client = new() { Id = "nzbget-1", Name = "NZBGet", Type = "nzbget" };

    private static Download Regrab(string? clientDownloadId)
    {
        var download = new Download
        {
            Id = "557630e8",
            Title = Title,
            DownloadClientId = Client.Id,
            StartedAt = DateTime.UtcNow
        };

        if (clientDownloadId is not null)
        {
            download.Metadata = new Dictionary<string, object> { ["ClientDownloadId"] = clientDownloadId };
        }

        return download;
    }

    private static QueueItem Job(string id, string status) => new() { Id = id, Title = Title, Status = status };

    [Fact]
    public void AnEarlierFailedJobOfTheSameNameDoesNotClaimARegrab()
    {
        // The re-grab is job 37705; 37615 is the first, wrong attempt, still in NZBGet's
        // history as DELETED/COPY. Matching it by title failed a download that had unpacked.
        var regrab = Regrab("37705");

        Assert.Null(DownloadQueueMetadataMatcher.FindBestMatchingDownload(
            Job("37615", "failed"), Client, [regrab], NullLogger.Instance));
        Assert.Same(regrab, DownloadQueueMetadataMatcher.FindBestMatchingDownload(
            Job("37705", "completed"), Client, [regrab], NullLogger.Instance));
    }

    [Fact]
    public void ADownloadWhoseJobIsNotYetKnownStillFindsItByTitle()
    {
        var unbound = Regrab(clientDownloadId: null);

        Assert.Same(unbound, DownloadQueueMetadataMatcher.FindBestMatchingDownload(
            Job("37705", "downloading"), Client, [unbound], NullLogger.Instance));
    }
}
