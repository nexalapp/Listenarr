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

namespace Listenarr.Domain.Audiobooks
{
    /// <summary>
    /// Turning a registered file's stored path into an absolute one.
    ///
    /// A stored path may be absolute or relative to the owning book's base path, so a
    /// bare join is not enough and neither is trusting the string as it stands. Shared
    /// rather than reimplemented per feature: conversion, tagging and anything else that
    /// opens a library file must agree on which file that is.
    /// </summary>
    public static class AudiobookFilePaths
    {
        public static string? ResolveFullPath(Audiobook audiobook, AudiobookFile file)
        {
            ArgumentNullException.ThrowIfNull(audiobook);
            ArgumentNullException.ThrowIfNull(file);

            return ResolveFullPath(audiobook.BasePath, file.Path);
        }

        public static string? ResolveFullPath(string? basePath, string? storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
            {
                return null;
            }

            try
            {
                if (Path.IsPathRooted(storedPath))
                {
                    return Path.GetFullPath(storedPath);
                }

                return string.IsNullOrWhiteSpace(basePath)
                    ? null
                    : Path.GetFullPath(Path.Combine(basePath, storedPath));
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }
    }
}
