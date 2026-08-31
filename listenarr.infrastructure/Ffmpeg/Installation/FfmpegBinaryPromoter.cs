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
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Ffmpeg.Installation
{
    /// <summary>
    /// Moves one extracted binary out of whatever subdirectory an archive happened to
    /// use and into the root of the bundled directory, then marks it executable.
    ///
    /// This used to be inlined for ffprobe alone, which is why the ffmpeg binary in the
    /// same archive was extracted and then abandoned: unreferenced, and without the
    /// executable bit. Both binaries now go through this.
    /// </summary>
    internal static class FfmpegBinaryPromoter
    {
        /// <summary>
        /// Promote <paramref name="binaryName"/> from anywhere under <paramref name="baseDir"/>
        /// to <paramref name="destinationPath"/>. Returns true when the binary ends up in place.
        /// </summary>
        public static async Task<bool> PromoteAsync(
            string baseDir,
            string binaryName,
            string destinationPath,
            IProcessRunner processRunner,
            ILogger logger)
        {
            var chosen = FindCandidate(baseDir, binaryName, logger);
            if (string.IsNullOrEmpty(chosen))
            {
                logger.LogInformation(
                    "No {Binary} binary found in extracted files under {BaseDir}",
                    binaryName,
                    baseDir);
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? baseDir);

                var chosenFull = Path.GetFullPath(chosen);
                var destFull = Path.GetFullPath(destinationPath);

                // Both ends of the move stay inside the bundled directory: the archive
                // chooses the source layout, so the extracted path is not trusted input.
                if (!FileSystemSafety.TryValidateMutationTarget(chosenFull, [baseDir], out chosenFull, out var chosenReason))
                {
                    logger.LogWarning(
                        "Blocked {Binary} candidate move. Candidate reason: {CandidateReason}",
                        binaryName,
                        LogRedaction.SanitizeText(chosenReason));
                    return false;
                }

                if (!FileSystemSafety.TryValidateMutationTarget(destFull, [baseDir], out destFull, out var destReason))
                {
                    logger.LogWarning(
                        "Blocked {Binary} candidate move. Destination reason: {DestinationReason}",
                        binaryName,
                        LogRedaction.SanitizeText(destReason));
                    return false;
                }

                if (FileUtils.AreFilesystemPathsEquivalentForCurrentOs(chosenFull, destFull))
                {
                    logger.LogInformation(
                        "{Binary} already extracted at destination {Dest}",
                        binaryName,
                        destFull);
                }
                else
                {
                    if (File.Exists(destFull))
                    {
                        try { File.Delete(destFull); }
                        catch (Exception ex) when (IsNonFatal(ex))
                        {
                            logger.LogDebug(ex, "Could not remove existing {Binary} before move", binaryName);
                        }
                    }

                    try
                    {
                        File.Move(chosenFull, destFull);
                        logger.LogInformation("Moved {Binary} from {Src} to {Dest}", binaryName, chosenFull, destFull);
                    }
                    catch (Exception mvEx) when (IsNonFatal(mvEx))
                    {
                        try
                        {
                            File.Copy(chosenFull, destFull, overwrite: true);
                            logger.LogInformation(
                                "Copied {Binary} from {Src} to {Dest} (move failed: {Err})",
                                binaryName,
                                chosenFull,
                                destFull,
                                mvEx.Message);
                        }
                        catch (Exception cpEx) when (IsNonFatal(cpEx))
                        {
                            logger.LogWarning(cpEx, "Failed to copy {Binary} from {Src} to {Dest}", binaryName, chosenFull, destFull);
                            return false;
                        }
                    }
                }

                await EnsureExecutableAsync(destFull, processRunner, logger);
                return File.Exists(destFull);
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogWarning(ex, "Failed to promote {Binary} into {Dest}", binaryName, destinationPath);
                return false;
            }
        }

        /// <summary>
        /// Mark a bundled binary executable. A static build arrives from the archive
        /// without the bit set on every extractor, and an unexecutable encoder is
        /// indistinguishable from a missing one at the call site.
        /// </summary>
        public static async Task EnsureExecutableAsync(
            string path,
            IProcessRunner processRunner,
            ILogger logger)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !File.Exists(path))
            {
                return;
            }

            try
            {
                var mode = File.GetUnixFileMode(path);
                File.SetUnixFileMode(
                    path,
                    mode
                        | UnixFileMode.UserExecute
                        | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherExecute);
                return;
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Managed chmod failed for {Path}; falling back to the chmod binary", path);
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("+x");
                psi.ArgumentList.Add(path);

                await processRunner.RunAsync(psi, 3000);
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Failed to mark {Path} executable", path);
            }
        }

        private static string? FindCandidate(string baseDir, string binaryName, ILogger logger)
        {
            try
            {
                var candidates = Directory
                    .GetFiles(baseDir, binaryName, SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(baseDir, "*" + binaryName + "*", SearchOption.AllDirectories))
                    .ToList();

                var exactMatches = candidates
                    .Where(p => string.Equals(Path.GetFileName(p), binaryName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (exactMatches.Count > 0)
                {
                    // Prefer a 'bin' directory, which is where full ffmpeg archives put them.
                    return exactMatches.FirstOrDefault(p =>
                               p.IndexOf(
                                   Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                                   StringComparison.OrdinalIgnoreCase) >= 0)
                           ?? exactMatches.OrderBy(p => p.Length).FirstOrDefault();
                }

                return candidates.OrderBy(p => p.Length).FirstOrDefault();
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Failed to enumerate {Binary} candidates under {BaseDir}", binaryName, baseDir);
                return null;
            }
        }

        private static bool IsNonFatal(Exception ex) =>
            ex is not OperationCanceledException
            && ex is not OutOfMemoryException
            && ex is not StackOverflowException;
    }
}
