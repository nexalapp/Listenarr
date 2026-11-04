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

using Listenarr.Application.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission
{
    /// <summary>
    /// Grabbing the NZB that a finished download turned out to contain, rather than the
    /// release itself.
    /// </summary>
    public partial class DownloadService
    {
        /// <summary>
        /// Submits an NZB found inside a finished Usenet download as the download it points
        /// to. Some uploaders post the real release under obfuscated names and an NZB of it
        /// under the forum's search code, so grabbing the code fetches only that NZB. It goes
        /// to the same client, for the same book, with the archive password the outer grab
        /// was given.
        /// </summary>
        public async Task<string> SendNestedNzbAsync(Download outer, string fileName, byte[] nzbBytes)
        {
            ArgumentNullException.ThrowIfNull(outer);
            ArgumentNullException.ThrowIfNull(nzbBytes);
            if (string.IsNullOrWhiteSpace(outer.DownloadClientId))
            {
                throw new DownloadClientSubmissionException("The finished download has no client to send its NZB to.");
            }

            var protectedPassword = outer.GetMetadataString(DownloadMetadataKeys.ProtectedArchivePassword);
            var password = string.IsNullOrWhiteSpace(protectedPassword) || secretProtector == null
                ? null
                : secretProtector.Unprotect(protectedPassword);
            var source = outer.GetMetadataString("Source") ?? "Nested NZB";
            var quality = outer.GetMetadataString("Quality");
            var locator = $"nested-nzb:{outer.Id}/{fileName}";

            var candidate = new TrustedDownloadCandidate(
                locator,
                outer.Title ?? fileName,
                outer.Artist ?? string.Empty,
                outer.Album ?? string.Empty,
                source,
                quality,
                outer.Language,
                0,
                null,
                new DownloadSourceDescriptor(
                    null,
                    null,
                    DownloadProtocol.Usenet,
                    [new DownloadSourceLocator(DownloadSourceLocatorKind.ReleaseId, locator)]));
            var prepared = new PreparedUsenetSubmission(
                candidate.Title,
                candidate.Artist,
                candidate.Album,
                source,
                quality,
                outer.Language,
                0,
                locator,
                nzbBytes,
                fileName,
                password);

            logger.LogInformation(
                "Submitting the NZB found inside download {DownloadId} ('{FileName}') as the release it points to",
                outer.Id,
                fileName);
            var downloadId = await SubmitPreparedAsync(
                Guid.NewGuid().ToString(),
                candidate,
                prepared,
                outer.DownloadClientId,
                outer.AudiobookId,
                isTorrent: false);

            var continued = await downloadRepository.FindAsync(downloadId);
            if (continued != null)
            {
                continued.Metadata ??= new Dictionary<string, object>();
                continued.Metadata[DownloadMetadataKeys.ContinuesFrom] = outer.Id;
                await UpdateAsync(continued);
            }

            return downloadId;
        }
    }
}
