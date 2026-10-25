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

namespace Listenarr.Domain.Audiobooks
{
    /// <summary>What a background check found for one suggested book.</summary>
    public enum SuggestionReleaseStatus
    {
        /// <summary>A release an indexer holds is, with high confidence, this book.</summary>
        Found = 0,

        /// <summary>Every indexer was asked and none had a release that is this book.</summary>
        NotFound = 1,

        /// <summary>
        /// Nothing could be learned: every indexer that was asked failed to answer.
        ///
        /// This is not the same as nobody having the book, and the difference matters once
        /// the answer decides whether a book is shown. An indexer having a bad night would
        /// otherwise read as "nothing holds this" and quietly hide a book that can be had.
        /// Stored as a number, so this member may be added without touching the rows above.
        /// </summary>
        Unknown = 2
    }

    /// <summary>
    /// Whether a suggested book can be downloaded, and the release to download.
    ///
    /// The Suggested page is for adding books in bulk, and a suggestion nothing holds is
    /// one more card to skip past. So each suggested ASIN is checked in the background
    /// and the answer kept here; the page hides what was not found, and adding a found
    /// book sends this release straight to the download client instead of searching
    /// again.
    /// </summary>
    public class SuggestionRelease
    {
        [Key]
        public int Id { get; set; }

        /// <summary>The suggested book's ASIN; every suggestion carries one.</summary>
        public string Asin { get; set; } = string.Empty;

        public SuggestionReleaseStatus Status { get; set; }

        /// <summary>The indexer that holds the release, for the page to name.</summary>
        public string? IndexerName { get; set; }

        /// <summary>The release's title as the indexer lists it.</summary>
        public string? ReleaseTitle { get; set; }

        public long? SizeBytes { get; set; }

        /// <summary>
        /// The indexer's search result, serialized and then protected: a Newznab result's
        /// NZB link carries the indexer's API key, which must not sit in the database in
        /// the clear.
        /// </summary>
        public string? ProtectedResult { get; set; }

        /// <summary>Why the answer is what it is, for diagnosis.</summary>
        public string? Detail { get; set; }

        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    }
}
