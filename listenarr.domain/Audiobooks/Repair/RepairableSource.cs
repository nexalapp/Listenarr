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

namespace Listenarr.Domain.Audiobooks.Repair
{
    /// <summary>
    /// Which of a book's files a repair would read: any audio file, the M4B included.
    ///
    /// Deliberately not <see cref="Conversion.ConvertibleSource"/>, which excludes .m4b
    /// because a book already in the output format has nothing to gain from a transcode.
    /// A repair is not a transcode — a damaged recording is damaged in whatever container
    /// it sits in, and a book already merged into one M4B is exactly as repairable as the
    /// MP3s it was merged from.
    /// </summary>
    public static class RepairableSource
    {
        public static bool IsRepairable(string? path) =>
            !string.IsNullOrWhiteSpace(path) && FileUtils.IsAudioFile(path);
    }
}
