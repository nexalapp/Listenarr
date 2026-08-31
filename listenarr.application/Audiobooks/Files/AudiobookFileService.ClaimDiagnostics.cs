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
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Files
{
    // Diagnostics helpers shared by the registration paths. Split out of
    // AudiobookFileService.cs to keep that file under the size cap the
    // architecture tests enforce.
    public partial class AudiobookFileService
    {
        private static string ResolveAbsolutePath(string? path) =>
            string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : FileSystemPathIdentity.ResolveNativeAbsolutePath(path);

        private void LogClaimRejection(
            int audiobookId,
            string path,
            AudiobookFileClaimResult claim)
        {
            var sanitizedPath = LogRedaction.SanitizeFilePath(path);
            if (claim.Outcome == AudiobookFileClaimOutcome.AlreadyOwnedByAudiobook)
            {
                logger.LogDebug(
                    "AudiobookFile already exists for audiobook {AudiobookId} at path {Path}",
                    audiobookId,
                    sanitizedPath);
                return;
            }

            logger.LogWarning(
                "Audiobook file ownership claim rejected for audiobook {AudiobookId} at {Path}: {Outcome}. {Reason}",
                audiobookId,
                sanitizedPath,
                claim.Outcome,
                claim.Reason);
        }

    }
}
