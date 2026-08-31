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
using System.Runtime.InteropServices;

namespace Listenarr.Infrastructure.Ffmpeg.Installation
{
    /// <summary>
    /// Finds an ffmpeg-family binary the host already provides.
    ///
    /// The bundled download cannot be relied on everywhere: the production image ships
    /// no ffmpeg, the dev container installs one through apt, and an operator may want
    /// to pin a build of their own. Resolution order is the explicit environment
    /// override, then PATH, then whatever was bundled by the installer.
    /// </summary>
    internal static class FfmpegPathLocator
    {
        /// <summary>
        /// Resolve an operator-supplied absolute path from <paramref name="environmentVariable"/>.
        /// Returns null when unset, relative, or not pointing at an existing file, so a
        /// typo falls through to discovery rather than disabling conversion outright.
        /// </summary>
        public static string? FromEnvironment(string environmentVariable)
        {
            var configured = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return null;
            }

            configured = configured.Trim();
            if (!Path.IsPathRooted(configured))
            {
                return null;
            }

            try
            {
                var full = Path.GetFullPath(configured);
                return File.Exists(full) ? full : null;
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>
        /// Search PATH for <paramref name="binaryName"/>. Entries are taken verbatim from
        /// the environment, so each candidate is combined and existence-checked rather
        /// than executed to probe it.
        /// </summary>
        public static string? FromSearchPath(string binaryName)
        {
            var searchPath = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(searchPath))
            {
                return null;
            }

            var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
            foreach (var entry in searchPath.Split(separator, StringSplitOptions.RemoveEmptyEntries))
            {
                var directory = entry.Trim();
                if (directory.Length == 0)
                {
                    continue;
                }

                try
                {
                    var candidate = Path.Combine(directory, binaryName);
                    if (!Path.IsPathRooted(candidate))
                    {
                        continue;
                    }

                    candidate = Path.GetFullPath(candidate);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception ex) when (
                    ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // A malformed PATH entry is not a reason to stop searching the rest.
                }
            }

            return null;
        }
    }
}
