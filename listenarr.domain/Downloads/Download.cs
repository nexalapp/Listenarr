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

using System.Text.Json;

namespace Listenarr.Domain.Downloads
{
    public enum DownloadStatus
    {
        Queued,
        Downloading,
        Paused,
        Completed,
        Failed,
        Processing,
        Ready,
        Moved,           // Added to track completed moves
        ImportBlocked,   // Added: Block re-processing of bad imports
        ImportPending    // Added: Explicitly waiting on import completion/manual interaction
    }

    public class Download
    {
        public static string METADATA_EXTERNAL_ID_KEY = "ClientDownloadId";
        public static int MaxImportAttempts = 3;

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int? AudiobookId { get; set; } // Link to Audiobook record for metadata
        public int? ActiveAudiobookDeduplicationKey { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;

        // Enhanced identifying metadata for better matching
        public string? Asin { get; set; } // Amazon identifier - most reliable
        public string? Isbn { get; set; } // ISBN - very reliable
        public string? Series { get; set; } // Series name
        public string? SeriesNumber { get; set; } // Book number in series
        public string? Publisher { get; set; }
        public string? Language { get; set; }
        public int? Runtime { get; set; } // Runtime in minutes
        public long? ExpectedFileSize { get; set; } // Expected file size from search result
        public string OriginalUrl { get; set; } = string.Empty;
        public DownloadStatus Status
        {
            get;
            set;
        } = DownloadStatus.Queued;

        /// <summary>
        /// Progress of the download.
        /// From 0 to 100 by convention
        /// </summary>
        public decimal Progress
        {
            get;
            set
            {
                field = Math.Min(value, 100);
            }
        }
        public long TotalSize { get; set; }
        public long DownloadedSize { get; set; }
        // Configured DownloadPath of the client
        public string DownloadPath { get; set; } = string.Empty;
        public string FinalPath { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// What went wrong, in the terms the next decision needs. Null until a download
        /// fails: the message beside it is the client's own words, while this is the
        /// classification the retry policy reads and the Activity row shows in place of
        /// the word "Failed".
        /// </summary>
        public DownloadFailureReason? FailureReason { get; set; }
        public string DownloadClientId { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = [];

        /// <summary>
        /// Tracks why a download is ImportBlocked (error code for categorization)
        /// Examples: "InvalidFormat", "MissingMetadata", "FileMoveError", "AlreadyImported"
        /// </summary>
        public string? ImportBlockReason { get; set; }

        /// <summary>
        /// List of user-facing error messages explaining why import failed
        /// Multiple messages can be added as validation discovers multiple issues
        /// </summary>
        public List<string>? ImportBlockMessages { get; set; }

        /// <summary>
        /// Number of import attempts made for this download
        /// Helps identify chronically failing imports
        /// </summary>
        public int ImportAttempts { get; set; } = 0;

        /// <summary>
        /// When the download was last imported successfully (for idempotency tracking)
        /// If null and Status=Moved, import happened but wasn't recorded
        /// </summary>
        public DateTime? LastImportedAt { get; set; }

        /// <summary>
        /// Unique identifier from download history to track import event
        /// Links to DownloadHistory.Id for audit trail
        /// </summary>
        public int? HistoryId { get; set; }

        public void SetMetadata(string key, object value)
        {
            Metadata[key] = value;
        }

        /// <summary>
        /// A metadata flag, false when it was never written.
        ///
        /// The dictionary comes back from the database as JSON, so a bool that went in as
        /// a bool comes out as a JsonElement; both forms are read here. A key that is
        /// absent reads as false, which is what every row written before a flag existed
        /// should mean.
        /// </summary>
        public bool GetMetadataBool(string key)
        {
            if (!Metadata.TryGetValue(key, out var value) || value == null)
            {
                return false;
            }

            return value switch
            {
                bool flag => flag,
                JsonElement { ValueKind: JsonValueKind.True } => true,
                JsonElement { ValueKind: JsonValueKind.False } => false,
                _ => bool.TryParse(value.ToString(), out var parsed) && parsed
            };
        }

        public string? GetMetadataString(string key)
        {
            if (!Metadata.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            if (value is JsonElement element)
            {
                return element.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.Undefined => null,
                    _ => element.ToString()
                };
            }

            return value.ToString();
        }

        public string? GetExternalId()
        {
            if (Metadata.TryGetValue(METADATA_EXTERNAL_ID_KEY, out var clientIdObj))
            {
                var clientId = clientIdObj?.ToString();
                if (!string.IsNullOrWhiteSpace(clientId))
                {
                    return clientId;
                }
            }

            // FIXME: Legacy specific for torrent client
            if (Metadata.TryGetValue("TorrentHash", out var torrentHashObj))
            {
                var torrentHash = torrentHashObj?.ToString();
                if (!string.IsNullOrWhiteSpace(torrentHash))
                {
                    return torrentHash;
                }
            }

            return null;
        }

        public void SetExternalId(string value)
        {
            Metadata[METADATA_EXTERNAL_ID_KEY] = value;
        }

        public Download SetStatus(DownloadStatus value)
        {
            if (IsValidStatusTransition(value))
            {
                Status = value;
            }
            else
            {
                throw new InvalidOperationException($"Download {Id} cannot go from {Status} to {value}");
            }

            return this;
        }

        public Download Importing()
        {
            SetStatus(DownloadStatus.ImportPending);
            return this;
        }

        public Download Imported()
        {
            SetStatus(DownloadStatus.Moved);
            return this;
        }

        public Download Failed(string message, DownloadFailureReason reason = DownloadFailureReason.Unknown)
        {
            SetStatus(DownloadStatus.Failed);
            ErrorMessage = message;
            FailureReason = reason;
            Metadata["ClientFailureReason"] = message;
            return this;
        }

        public Download Blocked(string reason, string message)
        {
            SetStatus(DownloadStatus.ImportBlocked);
            AddBlockMessage(message);
            ImportBlockReason = reason;
            return this;
        }

        public Download Unblock()
        {
            ImportBlockReason = null;
            ImportBlockMessages = null;
            ImportAttempts = 0;

            Importing();

            return this;
        }

        public Download Processing()
        {
            SetStatus(DownloadStatus.Processing);
            return this;
        }

        public Download Downloading()
        {
            SetStatus(DownloadStatus.Downloading);
            return this;
        }

        public Download Completed()
        {
            Progress = 100.0M;
            SetStatus(DownloadStatus.Completed);
            return this;
        }

        public bool IsBlocked()
        {
            return Status == DownloadStatus.ImportBlocked;
        }

        public void AddBlockMessage(string message)
        {
            if (ImportBlockMessages == null)
            {
                ImportBlockMessages = [];
            }
            ImportBlockMessages.Add(message);
        }

        private bool IsValidStatusTransition(DownloadStatus toStatus)
        {
            if (Status == toStatus)
            {
                return true;
            }

            return toStatus switch
            {
                DownloadStatus.ImportPending => Status is DownloadStatus.Queued
                    or DownloadStatus.Downloading
                    or DownloadStatus.Paused
                    or DownloadStatus.Processing
                    or DownloadStatus.Completed
                    or DownloadStatus.ImportBlocked,

                DownloadStatus.ImportBlocked => Status is DownloadStatus.ImportPending
                    or DownloadStatus.Processing
                    or DownloadStatus.Completed
                    or DownloadStatus.Downloading,

                DownloadStatus.Moved => Status is DownloadStatus.ImportPending
                    or DownloadStatus.Completed
                    or DownloadStatus.Processing
                    or DownloadStatus.Downloading,

                _ => true,
            };
        }

        public bool AwaitsImportation()
        {
            return Status == DownloadStatus.Completed ||
                Status == DownloadStatus.ImportPending;
        }

        public Download Clone()
        {
            return new Download
            {
                Id = Id,
                Title = Title,
                Artist = Artist,
                Album = Album,
                OriginalUrl = OriginalUrl,
                Status = Status,
                Progress = Progress,
                TotalSize = TotalSize,
                DownloadedSize = DownloadedSize,
                DownloadPath = DownloadPath,
                FinalPath = FinalPath,
                StartedAt = StartedAt,
                CompletedAt = CompletedAt,
                ErrorMessage = ErrorMessage,
                FailureReason = FailureReason,
                DownloadClientId = DownloadClientId
            };
        }
    }
}
