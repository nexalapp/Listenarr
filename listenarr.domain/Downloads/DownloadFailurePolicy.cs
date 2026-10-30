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

namespace Listenarr.Domain.Downloads
{
    /// <summary>What to do about one failed download.</summary>
    /// <param name="BlockRelease">
    /// Whether this release must never be grabbed for this book again. Only for failures
    /// the release itself causes: another attempt at it would fail the same way, and
    /// without the block the next search picks the same highest-scoring release and the
    /// book loops between searching and failing.
    /// </param>
    /// <param name="PurgeClientRecord">
    /// Whether the client's own record of the attempt has to be deleted outright.
    /// NZBGet keeps a hidden duplicate history and silently discards a later grab that
    /// looks like something in it, reporting DELETED/COPY without fetching anything -
    /// which is itself a failure, so the book loops. Deleting the record is the only way
    /// the next release gets a hearing. Listenarr keeps the reason on the download and in
    /// its own history, so nothing diagnostic is lost with it.
    /// </param>
    /// <param name="SearchAgain">
    /// Whether to look for another release at once. A local failure - no disk space, a
    /// move that could not be made - would stop the next release in the same place, so
    /// searching again just burns a grab.
    /// </param>
    public readonly record struct DownloadFailureResponse(
        bool BlockRelease,
        bool PurgeClientRecord,
        bool SearchAgain);

    /// <summary>
    /// What each kind of failure deserves.
    ///
    /// One table rather than a condition at each site, because the three decisions have
    /// to agree: blocking a release without searching again abandons the book, and
    /// searching again without blocking re-grabs what just failed.
    /// </summary>
    public static class DownloadFailurePolicy
    {
        /// <summary>
        /// How many automatic grabs one book may lose in a day before Listenarr stops
        /// trying. Each failure blocks a release, so the search does move on - but a book
        /// posted badly a dozen times over should not spend the night grabbing all of
        /// them. What is left is reported as failed and waits for a person.
        /// </summary>
        public const int MaxAutomaticAttemptsPerDay = 5;

        public static DownloadFailureResponse For(DownloadFailureReason reason) => reason switch
        {
            // The release is the problem, and another one may not be.
            DownloadFailureReason.Health or
            DownloadFailureReason.Damaged or
            DownloadFailureReason.Password =>
                new(BlockRelease: true, PurgeClientRecord: true, SearchAgain: true),

            // Nothing was fetched and the release is not implicated. Clearing the record
            // that caused the discard is the whole remedy.
            DownloadFailureReason.Duplicate =>
                new(BlockRelease: false, PurgeClientRecord: true, SearchAgain: true),

            // Local failures. Another release lands in the same place, so leave it to a
            // person and keep the client's record for them to look at.
            DownloadFailureReason.DiskSpace or
            DownloadFailureReason.PostProcessing =>
                new(BlockRelease: false, PurgeClientRecord: false, SearchAgain: false),

            // The client lost the files rather than the release being bad, so fetching it
            // again is reasonable and nothing needs blocking.
            DownloadFailureReason.MissingFiles =>
                new(BlockRelease: false, PurgeClientRecord: false, SearchAgain: true),

            // An unrecognised failure: searching again is what Listenarr has always done,
            // and blocking a release on a message nobody has classified would hide it for
            // good on a guess.
            _ => new(BlockRelease: false, PurgeClientRecord: false, SearchAgain: true)
        };
    }
}
