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
    /// <summary>
    /// How much of the work before a download has been done for a release.
    ///
    /// Stored by name, so renaming a member orphans every row holding the old one.
    /// </summary>
    public enum ReleasePreparationState
    {
        /// <summary>Found, but nothing has been resolved yet.</summary>
        NotPrepared = 0,

        /// <summary>An NZB is in hand; a grab is a send and nothing more.</summary>
        Ready = 1,

        /// <summary>
        /// The free indexes do not hold it and the metered one was not allowed to be
        /// asked. A grab the user asks for may still ask it.
        /// </summary>
        NeedsMeteredIndex = 2,

        /// <summary>
        /// No index holds the post's search string, or the post never yielded one.
        /// Grabbing it needs an NZB supplied by hand.
        /// </summary>
        Unresolvable = 3
    }

    /// <summary>What a background check found for one suggested book.</summary>
    public enum SuggestionReleaseStatus
    {
        /// <summary>A release an indexer holds is, with high confidence, this book.</summary>
        Found = 0,

        /// <summary>Every indexer was asked and none had a release that is this book.</summary>
        NotFound = 1
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

        /// <summary>
        /// How far the work of turning this release into a download has got.
        ///
        /// Everything short of sending it is done ahead of time, so pressing the button
        /// is a send rather than a search, a thanks and a resolve.
        /// </summary>
        public ReleasePreparationState Preparation { get; set; } = ReleasePreparationState.NotPrepared;

        /// <summary>
        /// The NZB this release resolved to, protected: the link carries an indexer's
        /// API key, which must not sit in the database in the clear.
        /// </summary>
        public string? ProtectedNzbUrl { get; set; }

        /// <summary>Which index produced the NZB — NZBIndex, Binsearch, NZBKing.</summary>
        public string? ResolvedVia { get; set; }

        /// <summary>
        /// A fingerprint of the post's payload at the moment it was resolved.
        ///
        /// Posters edit the payload in place when a release is replaced, and a stored
        /// NZB then points at the superseded upload. The grab re-reads the post — one
        /// free request — and only trusts what is stored when this still matches, so
        /// the saved work is used where it is valid and quietly redone where it is not.
        /// </summary>
        public string? PayloadFingerprint { get; set; }

        /// <summary>Why preparation stopped where it did, for the page to show.</summary>
        public string? PreparationDetail { get; set; }

        public DateTime? PreparedAt { get; set; }
    }
}
