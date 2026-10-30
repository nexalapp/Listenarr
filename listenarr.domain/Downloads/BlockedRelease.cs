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
using System.Text;

namespace Listenarr.Domain.Downloads
{
    /// <summary>
    /// A release that failed for its own reasons and must not be grabbed for this book
    /// again.
    ///
    /// Without this, a failed grab and an automatic search make a loop: the search scores
    /// the same releases in the same order, picks the same one, and it fails the same way.
    /// Blocking the release is what makes "try another source" mean anything, and when
    /// every release a book has is blocked the search runs out honestly rather than
    /// churning - which is also the signal that the book may genuinely not be obtainable.
    /// </summary>
    public class BlockedRelease
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// The book the release was grabbed for. A release is blocked for one book, not
        /// the library: the same posting can be the right bytes for another edition, and
        /// a reason misread once should not take a release away from every book at once.
        /// </summary>
        public int? AudiobookId { get; set; }

        /// <summary>The release's title as the indexer listed it.</summary>
        public string ReleaseTitle { get; set; } = string.Empty;

        /// <summary>
        /// The title reduced to what survives being re-indexed elsewhere, which is what
        /// later searches are compared against. Stored rather than computed on read so a
        /// change to the reduction cannot silently unblock what is already blocked.
        /// </summary>
        public string Fingerprint { get; set; } = string.Empty;

        /// <summary>The indexer the release came from, for the record rather than for matching.</summary>
        public string? Indexer { get; set; }

        /// <summary>The indexer's own id for the release, where it gave one.</summary>
        public string? ReleaseId { get; set; }

        public long? SizeBytes { get; set; }

        public DownloadFailureReason Reason { get; set; }

        /// <summary>What the client said, for a person reading the list later.</summary>
        public string? Detail { get; set; }

        /// <summary>The download that failed, so the two records can be read together.</summary>
        public string? DownloadId { get; set; }

        public DateTime BlockedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The block to record for a download that failed.
        ///
        /// The download row is all that is left of the release by then - the search
        /// results are long gone - so what identifies it has to come from there: the
        /// title it was submitted under, the indexer that offered it, the size it
        /// claimed, and the indexer's own id where the grab kept one.
        /// </summary>
        public static BlockedRelease For(
            Download download,
            DownloadFailureReason reason,
            string? detail)
        {
            ArgumentNullException.ThrowIfNull(download);

            return new BlockedRelease
            {
                AudiobookId = download.AudiobookId,
                ReleaseTitle = download.Title ?? string.Empty,
                Fingerprint = BlockedReleaseMatch.Fingerprint(download.Title),
                Indexer = download.GetMetadataString(ReleaseIndexerKey),
                ReleaseId = SafeReleaseId(download.GetMetadataString(ReleaseIdKey)),
                SizeBytes = download.TotalSize > 0 ? download.TotalSize : null,
                Reason = reason,
                Detail = detail,
                DownloadId = download.Id,
                BlockedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// An indexer's id for a release, when it is one that may be kept.
        ///
        /// Newznab lets an indexer put anything in a result's guid, and some put a link
        /// there. A link can carry the indexer's API key, which must never sit in the
        /// database in the clear - and a link is no use for recognising a release anyway,
        /// since it is the title and size that survive being listed somewhere else. So
        /// anything that looks like a URL or carries a query is dropped and the release is
        /// matched on its name.
        /// </summary>
        public static string? SafeReleaseId(string? candidateId)
        {
            var id = candidateId?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return id.Contains("://", StringComparison.Ordinal) ||
                id.Contains('?', StringComparison.Ordinal) ||
                id.Contains('&', StringComparison.Ordinal) ||
                id.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("api_key", StringComparison.OrdinalIgnoreCase)
                ? null
                : id;
        }

        /// <summary>The download metadata key holding the indexer a release came from.</summary>
        public const string ReleaseIndexerKey = "Source";

        /// <summary>The download metadata key holding the indexer's own id for the release.</summary>
        public const string ReleaseIdKey = "ReleaseId";
    }

    /// <summary>
    /// Deciding whether a release found now is one that already failed.
    ///
    /// A usenet posting indexed in three places is one set of articles: if it is too far
    /// gone to rebuild from one indexer's listing it is too far gone from all of them. So
    /// a match is on the release, not on the release-at-an-indexer, and the indexer is
    /// recorded but never compared.
    /// </summary>
    public static class BlockedReleaseMatch
    {
        /// <summary>
        /// Sizes reported for one posting differ a little between indexers - some count
        /// the par2 volumes, some round. A tenth is wide enough to survive that and
        /// narrow enough that two different releases sharing a title are still told
        /// apart.
        /// </summary>
        private const double SizeTolerance = 0.1;

        /// <summary>
        /// The release title reduced to letters and digits, lowercased: the separators,
        /// bracketing and padding that indexers add and strip differ between them, and
        /// none of it distinguishes one posting from another.
        /// </summary>
        public static string Fingerprint(string? releaseTitle)
        {
            if (string.IsNullOrWhiteSpace(releaseTitle))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(releaseTitle.Length);
            foreach (var character in releaseTitle)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Whether <paramref name="entry"/> is this release.
        ///
        /// An indexer's own id settles it when both sides have one and they agree. Failing
        /// that it is the fingerprint, corroborated by size where both are known: a title
        /// with nothing left after reduction matches nothing, because otherwise one
        /// unnamed release would block every unnamed release.
        /// </summary>
        public static bool Matches(
            BlockedRelease entry,
            string? candidateTitle,
            string? candidateReleaseId,
            long candidateSize)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (!string.IsNullOrWhiteSpace(entry.ReleaseId) &&
                !string.IsNullOrWhiteSpace(candidateReleaseId) &&
                string.Equals(entry.ReleaseId, candidateReleaseId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var fingerprint = Fingerprint(candidateTitle);
            if (fingerprint.Length == 0 || !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return false;
            }

            if (entry.SizeBytes is not > 0 || candidateSize <= 0)
            {
                return true;
            }

            var difference = Math.Abs(entry.SizeBytes.Value - candidateSize);
            return difference <= entry.SizeBytes.Value * SizeTolerance;
        }
    }
}
