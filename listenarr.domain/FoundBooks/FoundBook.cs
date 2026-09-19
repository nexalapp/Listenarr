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

namespace Listenarr.Domain.FoundBooks
{
    /// <summary>
    /// Whether the files on disk are the whole book. Decided by
    /// <see cref="FoundBookCompletenessAnalyzer"/> from what the files say about
    /// themselves; nothing here consults a catalogue.
    /// </summary>
    public enum FoundBookCompleteness
    {
        /// <summary>Nothing in the files says how many parts there should be, and nothing says any are missing.</summary>
        Unknown,

        /// <summary>At least one positive signal (a numbered run that closes, a declared length that matches) and no negative one.</summary>
        Complete,

        /// <summary>A numbered run with a gap, or a declared length the files fall short of.</summary>
        Incomplete,

        /// <summary>ffprobe could not read at least one audio file.</summary>
        Corrupt
    }

    /// <summary>Whether the library already holds what this looks like.</summary>
    public enum FoundBookLibraryStatus
    {
        /// <summary>No record matched by identifier or by title and author.</summary>
        New,

        /// <summary>A matching record has a file already.</summary>
        InLibrary,

        /// <summary>A matching record is monitored and has no file yet: this is what it is waiting for.</summary>
        Wanted
    }

    /// <summary>Why a <see cref="FoundBookState.Blocked"/> row is not offered.</summary>
    public enum FoundBookBlockedKind
    {
        None,

        /// <summary>The files changed since the last scan, or were written too recently.</summary>
        Settling,

        /// <summary>A download client is still writing into the folder.</summary>
        Downloading,

        /// <summary>
        /// A Listenarr download record still owns the files: the import pipeline has not
        /// finished with them, or they are a copy kept for seeding. Nothing here may
        /// move or delete them.
        /// </summary>
        OwnedByDownload
    }

    public enum FoundBookState
    {
        /// <summary>Offered on the Found tab and waiting for a decision.</summary>
        Pending,

        /// <summary>
        /// Seen but not offered: the files are still changing, or something else still
        /// owns them. <see cref="FoundBook.BlockedReason"/> says which.
        /// </summary>
        Blocked,

        /// <summary>The operator said no. Remembered by cluster key so it does not come back.</summary>
        Ignored,

        /// <summary>An import is in flight.</summary>
        Importing,

        /// <summary>Imported into the library; kept until the next scan confirms the files are gone.</summary>
        Imported,

        /// <summary>The files were deleted at the operator's request.</summary>
        Discarded
    }

    /// <summary>
    /// One book's worth of files found in a watch folder, waiting to be added, ignored
    /// or discarded.
    ///
    /// A row is identified by <see cref="ClusterKey"/>, which hashes the audio files'
    /// relative paths and nothing else, so a file still being written keeps its row
    /// across scans while <see cref="Signature"/> — which also covers size and mtime —
    /// is what says whether it has settled.
    /// </summary>
    public class FoundBook
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(64)]
        public string ClusterKey { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Signature { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string WatchFolder { get; set; } = string.Empty;

        /// <summary>The deepest directory that holds every one of the cluster's files.</summary>
        [MaxLength(2000)]
        public string BookFolder { get; set; } = string.Empty;

        /// <summary>
        /// The files as a JSON array of <see cref="FoundBookFileEntry"/>, audio first in
        /// playback order, then companions. Probe results ride along so an unchanged
        /// file is not read again on the next scan.
        /// </summary>
        public string FilesJson { get; set; } = "[]";

        public int AudioFileCount { get; set; }
        public long TotalBytes { get; set; }
        public double TotalDurationSeconds { get; set; }

        [MaxLength(16)]
        public string? Format { get; set; }

        [MaxLength(500)]
        public string? DetectedTitle { get; set; }

        [MaxLength(500)]
        public string? DetectedAuthor { get; set; }

        [MaxLength(500)]
        public string? DetectedSeries { get; set; }

        [MaxLength(32)]
        public string? DetectedSeriesPosition { get; set; }

        [MaxLength(500)]
        public string? DetectedNarrator { get; set; }

        [MaxLength(16)]
        public string? DetectedYear { get; set; }

        [MaxLength(32)]
        public string? DetectedAsin { get; set; }

        public FoundBookCompleteness Completeness { get; set; } = FoundBookCompleteness.Unknown;

        [MaxLength(1000)]
        public string? CompletenessReason { get; set; }

        public FoundBookLibraryStatus LibraryStatus { get; set; } = FoundBookLibraryStatus.New;

        /// <summary>The library record that <see cref="LibraryStatus"/> refers to, when there is one.</summary>
        public int? MatchedAudiobookId { get; set; }

        public FoundBookState State { get; set; } = FoundBookState.Blocked;

        public FoundBookBlockedKind BlockedKind { get; set; } = FoundBookBlockedKind.None;

        [MaxLength(500)]
        public string? BlockedReason { get; set; }

        public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        /// <summary>When <see cref="Signature"/> last changed; the stability gate reads this.</summary>
        public DateTime SignatureChangedAt { get; set; } = DateTime.UtcNow;

        public DateTime? DecidedAt { get; set; }
    }

    /// <summary>One file of a found book, as stored in <see cref="FoundBook.FilesJson"/>.</summary>
    public sealed record FoundBookFileEntry(
        string Path,
        long Length,
        DateTime LastWriteUtc,
        bool IsAudio,
        FoundBookProbeSnapshot? Probe);

    /// <summary>
    /// What one probe of an audio file said, kept with the file so a scan only reads
    /// files it has not seen at this size and mtime before.
    /// </summary>
    public sealed record FoundBookProbeSnapshot(
        bool Succeeded,
        string? Error,
        double DurationSeconds,
        int ChapterCount,
        int? TrackNumber,
        int? TrackTotal,
        string? Title,
        string? Album,
        string? Artist,
        string? AlbumArtist,
        string? Narrator,
        string? Series,
        string? SeriesPosition,
        string? Year,
        string? Asin,
        double? DeclaredDurationSeconds);
}
