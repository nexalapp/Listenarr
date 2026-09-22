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
    internal static class FfprobePlatformDefaults
    {
        public static string? GetDownloadUrl()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // BtbN's n8.1 builds rather than johnvansickle's, whose "release" is
                // 7.0.2 and whose git build is older still. The difference is xHE-AAC:
                // 7.0.2 will not open the decoder at all ("Function not implemented"),
                // so a book posted in it cannot be listened to, audited or converted.
                // 8.1 decodes it — with complaints on some packets, and audio out of
                // the rest, which is what those features need. Pinned to the 8.1 line
                // rather than master so a deploy does not follow a moving build.
                return RuntimeInformation.OSArchitecture == Architecture.Arm64
                    ? "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-linuxarm64-gpl-8.1.tar.xz"
                    : "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-linux64-gpl-8.1.tar.xz";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Must be the ffprobe archive: every caller looks for an "ffprobe" binary in the
                // extracted files, so the ffmpeg archive installs nothing and the scan then runs
                // without embedded-tag metadata. "getrelease" tracks the current build rather than
                // pinning a version that ages out.
                return "https://evermeet.cx/ffmpeg/getrelease/ffprobe/zip";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
            }

            return null;
        }

        /// <summary>
        /// URL of an archive that carries the ffmpeg encoder, when the ffprobe archive
        /// for this platform does not. Returns null where one download supplies both,
        /// so no second request is made for nothing.
        /// </summary>
        public static string? GetFfmpegDownloadUrl()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // The macOS ffprobe archive above holds ffprobe alone, so the encoder
                // has to be fetched separately. "getrelease" tracks the current build.
                return "https://evermeet.cx/ffmpeg/getrelease/ffmpeg/zip";
            }

            // The Linux static tarballs and the Windows essentials build both already
            // contain ffmpeg alongside ffprobe.
            return null;
        }

        public static string? GetChecksum()
        {
            return null;
        }
    }
}
