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
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Listenarr.Domain.Common;

namespace Listenarr.Domain.Audiobooks
{
    public class AudiobookFile
    {
        [Key]
        public int Id { get; set; }

        public int AudiobookId { get; set; }
        [JsonIgnore]
        public Audiobook? Audiobook { get; set; }

        // Stored path may be absolute or relative to the owning audiobook's BasePath.
        public string? Path { get; internal set; }

        public string? CanonicalPath { get; private set; }
        public FileSystemPathSyntax? PathSyntax { get; private set; }
        public FileSystemCaseSensitivity PathCaseSensitivity { get; private set; } = FileSystemCaseSensitivity.Unknown;
        public FileSystemCaseSensitivityMode PathCaseSensitivityMode { get; private set; } = FileSystemCaseSensitivityMode.Auto;
        public string? PathIdentityBoundary { get; private set; }
        public string? PathIdentityLookupKey { get; private set; }
        public string? PathOwnershipKey { get; private set; }
        public int PathIdentityVersion { get; private set; } = 1;
        public PathIdentityState PathIdentityState { get; private set; } = PathIdentityState.Unavailable;
        public string? PathIdentityReason { get; private set; }

        [JsonIgnore]
        public string? PhysicalObjectIdentity { get; private set; }
        [JsonIgnore]
        public int PhysicalIdentityVersion { get; private set; } = 1;
        [JsonIgnore]
        public DateTime? PhysicalIdentityObservedAtUtc { get; private set; }

        public static AudiobookFile CreateUnresolved(string? path = null)
        {
            var file = new AudiobookFile();
            file.Path = path;
            return file;
        }

        public void ApplyPathIdentity(
            string storedPath,
            AudiobookFilePathIdentity identity)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storedPath);
            ArgumentNullException.ThrowIfNull(identity);
            identity.Validate();

            Path = storedPath;
            CanonicalPath = identity.CanonicalPath;
            PathSyntax = identity.Syntax;
            PathCaseSensitivity = identity.CaseSensitivity;
            PathCaseSensitivityMode = identity.RequestedMode;
            PathIdentityBoundary = identity.BoundaryPath;
            PathIdentityLookupKey = identity.LookupKey;
            PathOwnershipKey = identity.OwnershipKey;
            PathIdentityVersion = identity.Version;
            PathIdentityState = identity.State;
            PathIdentityReason = identity.Reason;
        }

        public AudiobookFilePathState CapturePathState() =>
            new(
                Path,
                CanonicalPath,
                PathSyntax,
                PathCaseSensitivity,
                PathCaseSensitivityMode,
                PathIdentityBoundary,
                PathIdentityLookupKey,
                PathOwnershipKey,
                PathIdentityVersion,
                PathIdentityState,
                PathIdentityReason);

        public void RestorePathState(AudiobookFilePathState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            Path = state.StoredPath;
            CanonicalPath = state.CanonicalPath;
            PathSyntax = state.Syntax;
            PathCaseSensitivity = state.CaseSensitivity;
            PathCaseSensitivityMode = state.RequestedMode;
            PathIdentityBoundary = state.BoundaryPath;
            PathIdentityLookupKey = state.LookupKey;
            PathOwnershipKey = state.OwnershipKey;
            PathIdentityVersion = state.Version;
            PathIdentityState = state.State;
            PathIdentityReason = state.Reason;
        }

        public void ApplyPhysicalObjectIdentity(
            string objectIdentity,
            DateTime observedAtUtc)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(objectIdentity);
            if (observedAtUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(
                    "Physical identity observation time must be UTC.",
                    nameof(observedAtUtc));
            }

            PhysicalObjectIdentity = objectIdentity;
            PhysicalIdentityVersion = 1;
            PhysicalIdentityObservedAtUtc = observedAtUtc;
        }

        public void ClearPhysicalObjectIdentity()
        {
            PhysicalObjectIdentity = null;
            PhysicalIdentityVersion = 1;
            PhysicalIdentityObservedAtUtc = null;
        }

        public void PreparePathIdentityReconciliation(string reason)
        {
            PathOwnershipKey = null;
            PathIdentityState = PathIdentityState.Unavailable;
            PathIdentityReason = string.IsNullOrWhiteSpace(reason)
                ? "Audiobook file identity reconciliation is incomplete."
                : reason;
        }

        public void MarkPathIdentityUnavailable(string? storedPath, string reason)
        {
            Path = storedPath;
            CanonicalPath = null;
            PathSyntax = null;
            PathCaseSensitivity = FileSystemCaseSensitivity.Unknown;
            PathCaseSensitivityMode = FileSystemCaseSensitivityMode.Auto;
            PathIdentityBoundary = null;
            PathIdentityLookupKey = null;
            PathOwnershipKey = null;
            PathIdentityVersion = 1;
            PathIdentityState = PathIdentityState.Unavailable;
            PathIdentityReason = string.IsNullOrWhiteSpace(reason)
                ? "Audiobook file identity is unavailable."
                : reason;
        }

        // Size in bytes
        public long? Size { get; set; }

        // Duration in seconds
        public double? DurationSeconds { get; set; }

        // Format name (e.g., m4b, mp3, flac)
        public string? Format { get; set; }
        // Extracted container (e.g., M4B, MP4)
        public string? Container { get; set; }
        // Audio codec (e.g., aac, mp3, opus)
        public string? Codec { get; set; }

        // Bitrate in bits per second
        public int? Bitrate { get; set; }

        // Sample rate in Hz
        public int? SampleRate { get; set; }

        // Number of audio channels
        public int? Channels { get; set; }

        // When this file record was created
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Optional source or notes (e.g., DDL, qBittorrent, NZB)
        public string? Source { get; set; }

        /// <summary>
        /// Tags no write may touch on this file, named as in
        /// <see cref="Tagging.TagCatalog"/>.
        ///
        /// <para>
        /// Held on the file rather than on the book because that is the granularity the
        /// fault has: a book split into parts can have one part whose description was
        /// corrected by hand and five whose descriptions Listenarr should keep writing.
        /// A lock on the book would be unable to say that.
        /// </para>
        /// <para>
        /// Honoured by the planner, so it holds for automatic tagging on scan completion
        /// as much as for a write somebody asked for. A lock that only held for the
        /// button next to it would be undone by the next scan.
        /// </para>
        /// </summary>
        public List<string>? LockedTags { get; set; }

        /// <summary>
        /// Whether this file's path is frozen: neither its name nor the folder holding it
        /// may be changed by organizing.
        ///
        /// <para>
        /// The case it exists for is a book whose filenames carry something no metadata
        /// has. A collection of short stories arrives as one Audible title, so the record
        /// has one title and one series for the lot, and the only place the individual
        /// story names survive is the filenames somebody typed. Rendering the naming
        /// pattern over those files replaces four story names with four numbers, and
        /// nothing can put them back.
        /// </para>
        /// <para>
        /// Freezing the folder as well as the name follows from what a path is: moving the
        /// folder moves the file. So a book with any locked file keeps the folder it is
        /// in, and its unlocked files are still named by the pattern inside it.
        /// </para>
        /// </summary>
        public bool PathLocked { get; set; }
    }
}
