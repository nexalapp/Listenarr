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
namespace Listenarr.Application.Downloads.Import
{
    public partial class DownloadImportService
    {
        /// <summary>
        /// The best quality the book already holds, as the string the quality rules
        /// compare — what an arriving file has to beat to be worth importing.
        /// </summary>
        /// <remarks>
        /// A file's own <c>Format</c> is the starting point, a bitrate overrides it with
        /// the banded MP3 name the profiles are written in, and a file carrying neither is
        /// read from its path. Null when the book holds no files, which is the ordinary
        /// first import and means nothing has to be beaten.
        /// </remarks>
        private static string? DescribeBestExistingQuality(Audiobook audiobook, QualityProfile? profile)
        {
            if (audiobook.Files == null || audiobook.Files.Count == 0)
            {
                return null;
            }

            string? best = null;
            foreach (var file in audiobook.Files)
            {
                var quality = string.Empty;
                if (!string.IsNullOrEmpty(file.Format))
                {
                    quality = file.Format;
                }

                if (file.Bitrate.HasValue)
                {
                    var kilobits = file.Bitrate.Value / 1000;
                    if (kilobits >= 320) quality = "MP3 320kbps";
                    else if (kilobits >= 256) quality = "MP3 256kbps";
                    else if (kilobits >= 192) quality = "MP3 192kbps";
                    else if (kilobits >= 128) quality = "MP3 128kbps";
                }

                if (string.IsNullOrEmpty(quality) && !string.IsNullOrEmpty(file.Path))
                {
                    quality = ImportQualityEvaluator.Determine(null, file.Path);
                }

                if (string.IsNullOrEmpty(best))
                {
                    best = quality;
                }
                else if (!string.IsNullOrEmpty(quality)
                    && profile != null
                    && ImportQualityEvaluator.IsAcceptable(quality, best, profile))
                {
                    best = quality;
                }
            }

            return best;
        }
    }
}
