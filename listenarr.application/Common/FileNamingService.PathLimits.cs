/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
namespace Listenarr.Application.Common
{
    /// <summary>
    /// Path-length limits: a single name never over the filesystem's 255 bytes on any
    /// platform, and the whole path under MAX_PATH on Windows.
    /// </summary>
    public partial class FileNamingService
    {
        /// <summary>
        /// Windows MAX_PATH limit (260 chars including null terminator).
        /// We use 259 as the effective usable limit.
        /// </summary>
        private const int WindowsMaxPath = 259;

        /// <summary>
        /// Maximum length for a single path component (file or folder name) on NTFS / most filesystems.
        /// </summary>
        private const int MaxComponentLength = 255;

        /// <summary>
        /// Ensure the generated path does not exceed platform limits.
        /// On Windows: total path ≤ 259 chars, each component ≤ 255 chars.
        /// Truncates the longest non-root components first while preserving the file extension.
        /// </summary>
        public string EnsurePathWithinLimits(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return fullPath;

            // Every platform caps a single name at 255 bytes; a component over that cannot be
            // created at all, and a full-cast narrator list is exactly what overflows it.
            fullPath = FitComponentsToNameMax(fullPath);

            // Only enforce total-path limits on Windows; other platforms support much longer paths
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return fullPath;

            var originalPath = fullPath;

            // Split into root (e.g. "D:\") and component parts
            var root = Path.GetPathRoot(fullPath) ?? string.Empty;
            var withoutRoot = fullPath.Substring(root.Length);
            var parts = withoutRoot.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (parts.Count == 0)
                return fullPath;

            // Preserve the file extension on the last component
            var extension = Path.GetExtension(parts.Last());

            // --- Step 1: Enforce per-component limit (255 chars) ---
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Length <= MaxComponentLength)
                    continue;

                // Last component (filename): keep extension
                parts[i] = i == parts.Count - 1 && !string.IsNullOrEmpty(extension)
                    ? parts[i].Substring(0, MaxComponentLength - extension.Length) + extension
                    : parts[i].Substring(0, MaxComponentLength);
            }

            // --- Step 2: Enforce total path length ---
            // Iteratively shorten the longest non-root component until within limit
            const int maxIterations = 50; // safety valve
            for (int iter = 0; iter < maxIterations; iter++)
            {
                var currentPath = root + string.Join(Path.DirectorySeparatorChar.ToString(), parts);
                if (currentPath.Length <= WindowsMaxPath)
                    break;

                var excess = currentPath.Length - WindowsMaxPath;

                // Find the longest component (prefer earlier components for ties, but skip tiny ones)
                int longestIdx = -1;
                int longestLen = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    var effectiveLen = (i == parts.Count - 1 && !string.IsNullOrEmpty(extension))
                        ? parts[i].Length - extension.Length
                        : parts[i].Length;

                    if (effectiveLen > longestLen)
                    {
                        longestLen = effectiveLen;
                        longestIdx = i;
                    }
                }

                if (longestIdx < 0 || longestLen <= 1)
                {
                    // Nothing left to truncate
                    _logger.LogWarning("Cannot shorten path below Windows MAX_PATH limit ({Limit} chars). Path length: {Length}. Path: {Path}",
                        WindowsMaxPath, currentPath.Length, currentPath);
                    break;
                }

                var part = parts[longestIdx];
                bool isFilename = longestIdx == parts.Count - 1 && !string.IsNullOrEmpty(extension);
                var nameWithoutExt = isFilename ? part.Substring(0, part.Length - extension.Length) : part;

                var newLen = Math.Max(1, nameWithoutExt.Length - excess);
                parts[longestIdx] = isFilename
                    ? nameWithoutExt.Substring(0, newLen).TrimEnd() + extension
                    : nameWithoutExt.Substring(0, newLen).TrimEnd();
            }

            var result = root + string.Join(Path.DirectorySeparatorChar.ToString(), parts);

            if (result != originalPath)
            {
                _logger.LogWarning("Path truncated to fit Windows MAX_PATH limit ({Limit} chars). Original length: {OriginalLength}, New length: {NewLength}. Truncated path: {Path}",
                    WindowsMaxPath, originalPath.Length, result.Length, result);
            }

            return result;
        }

        private string FitComponentsToNameMax(string fullPath)
        {
            var separator = fullPath.Contains('/') && !fullPath.Contains('\\') ? '/' : Path.DirectorySeparatorChar;
            var parts = fullPath.Split(separator);
            var changed = false;
            for (var i = 0; i < parts.Length; i++)
            {
                var isLast = i == parts.Length - 1;
                var fitted = NarratorNameStyle.FitComponent(parts[i], isLast ? Path.GetExtension(parts[i]) : string.Empty);
                if (!ReferenceEquals(fitted, parts[i]) && fitted != parts[i])
                {
                    _logger.LogWarning(
                        "Path component over {Limit} bytes was shortened: {Before} -> {After}",
                        NarratorNameStyle.MaxComponentBytes,
                        parts[i],
                        fitted);
                    parts[i] = fitted;
                    changed = true;
                }
            }

            return changed ? string.Join(separator, parts) : fullPath;
        }
    }
}
