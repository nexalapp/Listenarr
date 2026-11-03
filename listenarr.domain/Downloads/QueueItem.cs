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

using System.Text.Json.Serialization;
using Listenarr.Domain.Common;

namespace Listenarr.Domain.Downloads
{
    /// <summary>
    /// Represents a download item in the queue with live status from download clients
    /// </summary>
    public class QueueItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Author { get; set; }
        public string? Series { get; set; }
        public string? SeriesNumber { get; set; }
        public string Quality { get; set; } = string.Empty;
        public string? Language { get; set; }
        public string Status { get; set; } = string.Empty; // downloading, paused, queued, completed, failed
        public double Progress { get; set; } // 0-100
        public long Size { get; set; } // in bytes
        public long Downloaded { get; set; } // in bytes
        public double DownloadSpeed { get; set; } // bytes per second
        public int? Eta { get; set; } // seconds remaining
        public string? Indexer { get; set; }
        public string DownloadClient { get; set; } = string.Empty;
        public string DownloadClientId { get; set; } = string.Empty;
        public string DownloadClientType { get; set; } = string.Empty; // qbittorrent, transmission, etc
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// What went wrong, classified. The client's message says it in its own words;
        /// this is what the Activity row shows in place of "Failed".
        /// </summary>
        public DownloadFailureReason? FailureReason { get; set; }
        public bool IsStaleSnapshot { get; set; }
        public string? SnapshotState { get; set; } // live, cached
        public string? SnapshotFailureReason { get; set; } // timeout, error, canceled
        public int? SnapshotAgeSeconds { get; set; }
        public DateTime? SnapshotRefreshedAt { get; set; }
        public bool CanPause { get; set; } = true;
        public bool CanRemove { get; set; } = true;
        public int? Seeders { get; set; }
        public int? Leechers { get; set; }
        public double? Ratio { get; set; }

        /// <summary>
        /// Link to the audiobook record this download is associated with (if any).
        /// </summary>
        public int? AudiobookId { get; set; }

        /// <summary>
        /// The path as reported by the download client (may be in different mount point)
        /// </summary>
        public string? RemotePath { get; set; }

        /// <summary>
        /// The path translated for Listenarr's filesystem (after applying remote path mapping)
        /// </summary>
        public string? LocalPath { get; set; }

        /// <summary>
        /// The full path to the downloaded content (file or folder).
        /// For qBittorrent: content_path field (save_path + name)
        /// This is often the most accurate path for locating the actual downloaded file/folder.
        /// </summary>
        public string? ContentPath { get; set; }

        /// <summary>
        /// Exact source files reported by the download client for this download.
        /// Used internally to scope automatic imports to files that actually belong
        /// to the download, even when the surrounding directory contains unrelated files.
        /// </summary>
        [JsonIgnore]
        public List<string>? SourceFiles { get; set; }

        /// <summary>
        /// The time when the download was detected as complete.
        /// Used for stability window validation before import.
        /// </summary>
        public DateTime? CompletionTime { get; set; }

        /// <summary>
        /// Raw client failure reason used for retry policy decisions. This is hidden from
        /// API consumers because ErrorMessage owns the user-facing text.
        /// </summary>
        [JsonIgnore]
        public string? ClientFailureReason { get; set; }

        /// <summary>
        /// Creates a shallow copy of this QueueItem.
        /// Used by GetImportItem to avoid modifying the original item.
        /// Matches DownloadClientItem.Clone() pattern.
        /// </summary>
        public QueueItem Clone()
        {
            return (QueueItem)MemberwiseClone();
        }

        public int GetMatchScore(Download download)
        {
            if (download == null)
            {
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(download.Id) &&
                !string.IsNullOrWhiteSpace(Id) &&
                string.Equals(download.Id, Id, StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            var clientDownloadId = download.GetMetadataString("ClientDownloadId");
            if (!string.IsNullOrWhiteSpace(clientDownloadId) &&
                string.Equals(clientDownloadId, Id, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            var torrentHash = download.GetMetadataString("TorrentHash");
            if (!string.IsNullOrWhiteSpace(torrentHash) &&
                string.Equals(torrentHash, Id, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (TitleUtils.TitlesExactlyMatch(download.Title, Title))
            {
                return 2;
            }

            if (TitleUtils.IsMatchingTitle(download.Title, Title))
            {
                if (TitleContainsArtist(download))
                {
                    return 2;
                }

                if (TitleUtils.IsStrongStandaloneTitleMatch(download.Title, Title))
                {
                    return 1;
                }
            }

            return 0;
        }

        public bool TitleContainsArtist(Download download)
        {
            var normalizedArtist = TitleUtils.NormalizeTitle(download?.Artist ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedArtist) || normalizedArtist.Length < 4)
            {
                return false;
            }

            var normalizedQueueTitle = TitleUtils.NormalizeTitle(Title ?? string.Empty);
            return normalizedQueueTitle.Contains(normalizedArtist, StringComparison.OrdinalIgnoreCase);
        }
    }
}
