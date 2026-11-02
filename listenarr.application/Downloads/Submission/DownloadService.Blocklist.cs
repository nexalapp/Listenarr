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

using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission
{
    /// <summary>
    /// The one gate every grab passes through.
    ///
    /// The automatic search filters blocked releases out of its own ranking, which only
    /// helps the caller that searches. A release prepared ahead of time, an abook.link
    /// topic sent by hand and a script posting to /download/send all reach the client
    /// without going near that ranking, so the block has to be enforced at submission or
    /// it is not enforced at all. Measured on this library: a release whose par2 set is
    /// 420 blocks short, and therefore unrepairable, was grabbed nine times in two days
    /// with its block sitting in the table the whole time.
    ///
    /// Kept beside the submission path rather than in it, because that file is at the size
    /// BackendArchitectureTests enforces.
    /// </summary>
    public partial class DownloadService
    {
        /// <summary>
        /// Whether this release is one that already failed for this book.
        ///
        /// A read that cannot be made lets the grab through rather than stopping it: the
        /// cost of being wrong that way is a single wasted grab, which the failure handler
        /// will block again, while the other way round a transient database error would
        /// quietly stop a book being downloaded at all.
        /// </summary>
        private async Task<bool> ReleaseIsBlockedAsync(int audiobookId, TrustedDownloadCandidate candidate)
        {
            try
            {
                var blocked = new ReleaseBlocklist(
                        await releaseBlocklistRepository.GetForAudiobookAsync(audiobookId))
                    .Find(candidate.Title, candidate.Id, candidate.Size);
                if (blocked == null)
                {
                    return false;
                }

                logger.LogWarning(
                    "Refusing to grab '{Title}' for audiobook {AudiobookId}: this release was blocked on {BlockedAt:u} after a {Reason} failure. {Detail}",
                    LogRedaction.SanitizeText(candidate.Title),
                    audiobookId,
                    blocked.BlockedAt,
                    blocked.Reason,
                    blocked.Detail);
                return true;
            }
            catch (Exception exception) when (exception is not (
                OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                logger.LogWarning(
                    exception,
                    "Could not read the release blocklist for audiobook {AudiobookId}; allowing the grab",
                    audiobookId);
                return false;
            }
        }
    }
}
