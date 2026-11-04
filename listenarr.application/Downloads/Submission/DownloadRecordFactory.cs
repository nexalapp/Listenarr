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

namespace Listenarr.Application.Downloads.Submission
{
    internal static class DownloadRecordFactory
    {
        public static Download CreateQueuedDownload(
            string downloadId,
            TrustedDownloadCandidate candidate,
            PreparedDownloadSubmission submission,
            DownloadClientConfiguration downloadClient,
            string downloadClientId,
            int? audiobookId,
            bool automatic = false)
        {
            return new Download
            {
                Id = downloadId,
                AudiobookId = audiobookId,
                Title = candidate.Title,
                Artist = candidate.Artist,
                Album = candidate.Album,
                Language = candidate.Language,
                OriginalUrl = submission.OriginalLocator,
                Progress = 0,
                TotalSize = candidate.Size,
                // The size the indexer advertised, kept apart from TotalSize because the
                // client overwrites that with its own figure once it has the job. For
                // Usenet the two differ by the par2 set - about a tenth of the posting -
                // and only this one can be compared with a later search result.
                ExpectedFileSize = candidate.Size > 0 ? candidate.Size : null,
                DownloadedSize = 0,
                DownloadPath = downloadClient.DownloadPath ?? string.Empty,
                FinalPath = string.Empty,
                StartedAt = DateTime.UtcNow,
                DownloadClientId = downloadClientId,
                Metadata = new Dictionary<string, object>
                {
                    [BlockedRelease.ReleaseIndexerKey] = candidate.Source,
                    // Kept so a release that fails can be blocked by the indexer's own
                    // id, which survives a title an indexer lists differently. An id that
                    // is really a link is dropped: it may carry the indexer's API key.
                    [BlockedRelease.ReleaseIdKey] = BlockedRelease.SafeReleaseId(candidate.Id) ?? string.Empty,
                    ["Seeders"] = candidate.Seeders ?? 0,
                    // Kept so this library can answer for itself whether a release's grab
                    // count predicts whether it imports. It counts fetches, not successes,
                    // so nothing reads it yet.
                    ["Grabs"] = candidate.Grabs ?? 0,
                    ["Quality"] = candidate.Quality ?? string.Empty,
                    ["Language"] = candidate.Language ?? string.Empty,
                    ["DownloadType"] = submission.Protocol.ToString(),
                    // Whether this library asked for the book or merely took what was
                    // offered. A grab nobody asked for has to answer for itself: it finds
                    // a replacement or the book is marked unobtainable, and either way its
                    // failure leaves the queue. A grab asked for by hand stays, because
                    // the person who asked is tracking it.
                    [DownloadMetadataKeys.AutomaticGrab] = automatic
                }
            };
        }
    }
}
