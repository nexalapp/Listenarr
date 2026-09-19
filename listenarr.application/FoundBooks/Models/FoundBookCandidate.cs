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
using Listenarr.Domain.FoundBooks;

namespace Listenarr.Application.FoundBooks.Models
{
    /// <summary>
    /// One cluster of files the scanner decided is a book, before it is reconciled
    /// against what was seen last time and against the library.
    /// </summary>
    public sealed record FoundBookCandidate(
        string WatchFolder,
        string BookFolder,
        string ClusterKey,
        string Signature,
        IReadOnlyList<FoundBookFileEntry> Files,
        string? Title,
        string? Author,
        string? Series,
        string? SeriesPosition,
        string? Narrator,
        string? Year,
        string? Asin,
        FoundBookCompleteness Completeness,
        string CompletenessReason,
        DateTime NewestWriteUtc,
        bool DownloadInProgress)
    {
        public IEnumerable<FoundBookFileEntry> AudioFiles => Files.Where(f => f.IsAudio);
        public int AudioFileCount => Files.Count(f => f.IsAudio);
        public long TotalBytes => Files.Sum(f => f.Length);
        public double TotalDurationSeconds => AudioFiles.Sum(f => f.Probe?.DurationSeconds ?? 0);
        public string? Format => AudioFiles
            .Select(f => Path.GetExtension(f.Path).TrimStart('.').ToUpperInvariant())
            .FirstOrDefault(ext => ext.Length > 0);
    }

    /// <summary>What the scanner already knows about a file from a previous scan.</summary>
    public sealed record FoundBookKnownFile(long Length, DateTime LastWriteUtc, FoundBookProbeSnapshot Probe);

    /// <summary>
    /// One folder's scan. <c>Succeeded</c> is false when the folder could not be listed
    /// at all; the caller must then leave last time's rows alone rather than read an
    /// empty result as "everything is gone".
    /// </summary>
    public sealed record FoundBookScanReport(
        string WatchFolder,
        IReadOnlyList<FoundBookCandidate> Candidates,
        IReadOnlyList<string> Warnings,
        bool Succeeded = true);
}
