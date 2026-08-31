/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;
namespace Listenarr.Application.Common
{
    public partial class FileNamingService
    {
        /// <summary>
        /// Remove invalid characters from path components.
        /// </summary>
        private string SanitizePathComponent(string pathComponent)
        {
            if (string.IsNullOrWhiteSpace(pathComponent))
            {
                return "Unknown";
            }

            var sanitized = new StringBuilder();
            foreach (var c in pathComponent)
            {
                if (char.IsControl(c))
                {
                    continue;
                }

                if (c == ':' || c == '/' || c == '\\')
                {
                    sanitized.Append(" - ");
                }
                else if (PortableInvalidFileNameChars.Contains(c))
                {
                    sanitized.Append('_');
                }
                else
                {
                    sanitized.Append(c);
                }
            }

            var result = sanitized.ToString();
            result = Regex.Replace(result, @"\s+", " ");
            result = Regex.Replace(result, @"(?:\s*-\s*){2,}", " - ");
            result = Regex.Replace(result, @"_+", "_");
            result = result.Trim();
            result = result.TrimEnd('.', ' ');
            result = Regex.Replace(result, @"^\s*[-_]+\s*", string.Empty);
            result = Regex.Replace(result, @"\s*[-_]+\s*$", string.Empty);

            if (string.IsNullOrWhiteSpace(result))
            {
                return "Unknown";
            }

            var extensionSeparator = result.IndexOf('.');
            var deviceNameStem = extensionSeparator >= 0 ? result[..extensionSeparator] : result;
            if (ReservedWindowsDeviceNames.Contains(deviceNameStem))
            {
                result = extensionSeparator >= 0
                    ? deviceNameStem + "_" + result[extensionSeparator..]
                    : result + "_";
            }

            return result;
        }

        private Dictionary<string, object> BuildVariables(AudioMetadata metadata) =>
            BuildVariables(metadata, sanitizeForPath: true);

        /// <summary>
        /// Resolve the pattern tokens from one book's metadata.
        ///
        /// <paramref name="sanitizeForPath"/> is what separates a name from a tag. A path
        /// component cannot hold a colon or a slash, so naming replaces them; a tag can,
        /// and must, or "Book Two: The Reckoning" becomes "Book Two - The Reckoning" in
        /// the file's own title. Nothing else differs between the two.
        /// </summary>
        private Dictionary<string, object> BuildVariables(AudioMetadata metadata, bool sanitizeForPath)
        {
            string Clean(string? value) =>
                string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : sanitizeForPath ? SanitizePathComponent(value) : StripControlCharacters(value);

            return new Dictionary<string, object>
            {
                // Keep multi-word author names as a single folder name (e.g. "Jane Austen")
                { "Author", Clean(FirstNonEmpty(ChooseAuthor(metadata), "Unknown Author")) },
                // For Series we must not fallback to Album or Title - when Series is blank we want
                // the variable to be empty so ApplyNamingPattern can remove any adjacent separators
                { "Series", Clean(metadata.Series) },
                { "Title", Clean(FirstNonEmpty(metadata.Title, "Unknown Title")) },
                { "Subtitle", Clean(metadata.Subtitle) },
                { "Edition", Clean(metadata.Edition) },
                { "Narrator", Clean(metadata.Narrator) },
                { "Publisher", Clean(metadata.Publisher) },
                { "Language", Clean(metadata.Language) },
                { "Asin", Clean(metadata.Asin) },
                { "Genre", Clean(metadata.Genre) },
                // Never sanitised as a path component: a blurb is several sentences of
                // real punctuation, and it is only ever written into a tag.
                { "Description", StripControlCharacters(metadata.Description, keepNewlines: true) },
                // Every series the book is in, each in its own bracket group, which is the
                // shape the library's multi-series files already use. Empty for a
                // standalone book so the surrounding pattern collapses around it.
                { "SeriesBrackets", BuildSeriesBrackets(metadata, sanitizeForPath) },
                // Prefer the position exactly as the source gave it. A non-numeric but real
                // position (an omnibus at "1-4") does not survive the decimal parse, and
                // falling through to TrackNumber here would write a track number into the
                // filename as if it were the series number.
                { "SeriesNumber", FirstNonEmpty(metadata.SeriesPositionRaw, metadata.SeriesPosition?.ToString(CultureInfo.InvariantCulture), metadata.TrackNumber?.ToString()) },
                { "Year", FirstNonEmpty(metadata.Year?.ToString()) },
                { "Quality", FirstNonEmpty(metadata.BitRate.HasValue ? metadata.BitRate + "kbps" : null, metadata.Format) },
                { "DiskNumber", metadata.DiscNumber?.ToString() ?? string.Empty },
                { "ChapterNumber", metadata.TrackNumber?.ToString() ?? string.Empty }
            };
        }

        /// <summary>
        /// Render every series the book belongs to as adjacent bracket groups —
        /// <c>[Enderverse 07.5][Ender's Saga 1.1]</c> — primary first.
        ///
        /// <para>
        /// Returns empty for a book with no series, so a pattern such as
        /// <c>{SeriesBrackets} {Title}</c> collapses to the bare title rather than
        /// leaving a stray separator. A single-series book renders one group, which is
        /// byte-identical to what <c>[{Series} {SeriesNumber}]</c> produces — so the same
        /// pattern serves all three shapes without a conditional.
        /// </para>
        /// <para>
        /// Bracket characters inside a series name are dropped rather than escaped: they
        /// would otherwise be read as group delimiters by the empty-group collapse and
        /// could swallow the title.
        /// </para>
        /// </summary>
        private string BuildSeriesBrackets(AudioMetadata metadata, bool sanitizeForPath)
        {
            var series = metadata.AllSeries;
            if (series == null || series.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(metadata.Series))
                {
                    return string.Empty;
                }

                series =
                [
                    new SeriesReference(
                        metadata.Series,
                        FirstNonEmpty(
                            metadata.SeriesPositionRaw,
                            metadata.SeriesPosition?.ToString(CultureInfo.InvariantCulture)))
                ];
            }

            return BuildSeriesBrackets(series, sanitizeForPath);
        }

        private string BuildSeriesBrackets(
            IReadOnlyList<SeriesReference> series,
            bool sanitizeForPath)
        {
            var builder = new StringBuilder();
            foreach (var entry in series)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                var name = StripBrackets(sanitizeForPath
                    ? SanitizePathComponent(entry.Name)
                    : StripControlCharacters(entry.Name));
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var number = StripBrackets(StripControlCharacters(entry.Number));

                builder.Append('[').Append(name);
                if (!string.IsNullOrWhiteSpace(number))
                {
                    builder.Append(' ').Append(number);
                }

                builder.Append(']');
            }

            return builder.ToString();
        }

        private static string StripBrackets(string? value) =>
            string.IsNullOrEmpty(value)
                ? string.Empty
                : new string([.. value.Where(c => c is not ('[' or ']'))]).Trim();

        /// <summary>
        /// Remove characters that cannot appear in a tag value without corrupting the
        /// metadata document that carries it, while leaving everything a reader would
        /// expect to see. Newlines survive only where they are meaningful — a blurb has
        /// paragraphs; an album name does not.
        /// </summary>
        private static string StripControlCharacters(string? value, bool keepNewlines = false)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (c == '\r')
                {
                    continue;
                }

                if (c == '\n')
                {
                    builder.Append(keepNewlines ? '\n' : ' ');
                    continue;
                }

                if (char.IsControl(c))
                {
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString().Trim();
        }

        private Dictionary<string, object> BuildVariables(AudibleBookMetadata metadata)
        {
            var author = metadata.Author ?? "Unknown Author";
            if (metadata.Authors != null && metadata.Authors.Count > 0)
            {
                // Assume first one is the main author
                author = metadata.Authors.First();
            }

            return new Dictionary<string, object>
            {
                { "Author", SanitizePathComponent(author) },
                { "Series", string.IsNullOrWhiteSpace(metadata.Series) ? string.Empty : SanitizePathComponent(metadata.Series) },
                { "Title", SanitizePathComponent(FirstNonEmpty(metadata.Title, "Unknown Title")) },
                { "Subtitle", string.IsNullOrWhiteSpace(metadata.Subtitle) ? string.Empty : SanitizePathComponent(metadata.Subtitle) },
                { "Edition", string.IsNullOrWhiteSpace(metadata.Edition) ? string.Empty : SanitizePathComponent(metadata.Edition) },
                { "Narrator", string.IsNullOrWhiteSpace(metadata.Narrator) ? string.Empty : SanitizePathComponent(metadata.Narrator) },
                { "Publisher", string.IsNullOrWhiteSpace(metadata.Publisher) ? string.Empty : SanitizePathComponent(metadata.Publisher) },
                { "Language", string.IsNullOrWhiteSpace(metadata.Language) ? string.Empty : SanitizePathComponent(metadata.Language) },
                { "Asin", string.IsNullOrWhiteSpace(metadata.Asin) ? string.Empty : SanitizePathComponent(metadata.Asin) },
                { "Genre", SanitizeOrEmpty(metadata.Genres?.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g))) },
                { "Description", StripControlCharacters(metadata.Description, keepNewlines: true) },
                { "SeriesBrackets", BuildSeriesBrackets(
                    AudiobookSeriesMembershipHelper
                        .Normalize(metadata.SeriesMemberships, metadata.Series, metadata.SeriesNumber)
                        .Select(m => new SeriesReference(m.SeriesName!, m.SeriesNumber))
                        .ToList(),
                    sanitizeForPath: true) },
                { "SeriesNumber", metadata.SeriesNumber?.ToString() ?? string.Empty },
                { "Year", metadata.PublishYear?.ToString() ?? string.Empty },
                { "Quality", string.Empty },
                { "DiskNumber", string.Empty },
                { "ChapterNumber", string.Empty }
            };
        }

        private string SanitizeOrEmpty(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : SanitizePathComponent(value);

        private static string FirstNonEmpty(params string?[] candidates)
        {
            return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;
        }

        // Heuristic: sometimes metadata.Artist can contain the title/series (noisy tags).
        // Prefer an AlbumArtist or alternate artist value if the primary artist looks like the title/series.
        private static string ChooseAuthor(AudioMetadata metadata)
        {
            var primary = NonNarratorAuthorCandidate(metadata.Artist, metadata.Narrator);
            var alternate = NonNarratorAuthorCandidate(metadata.AlbumArtist, metadata.Narrator);

            if (string.IsNullOrWhiteSpace(primary))
            {
                return alternate;
            }

            if (!string.IsNullOrWhiteSpace(metadata.Title) &&
                (primary.IndexOf(metadata.Title, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (!string.IsNullOrWhiteSpace(metadata.Series) && string.Equals(primary, metadata.Series, StringComparison.OrdinalIgnoreCase)) ||
                 string.Equals(primary, metadata.Title, StringComparison.OrdinalIgnoreCase)))
                return !string.IsNullOrWhiteSpace(alternate) ? alternate : primary;

            return string.IsNullOrWhiteSpace(primary) ? alternate : primary;
        }

        private static string NonNarratorAuthorCandidate(string? candidate, string? narrator)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            var trimmedCandidate = candidate.Trim();
            if (!string.IsNullOrWhiteSpace(narrator) &&
                string.Equals(trimmedCandidate, narrator.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return trimmedCandidate;
        }

        private static HashSet<char> BuildPortableInvalidFileNameChars()
        {
            var invalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());

            foreach (var c in "<>:\"/\\|?*")
            {
                invalidChars.Add(c);
            }

            for (int i = 0; i < 32; i++)
            {
                invalidChars.Add((char)i);
            }

            return invalidChars;
        }

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

            // Only enforce strict limits on Windows; other platforms support much longer paths
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

        private static string CombineWithOptionalBase(string? basePath, string candidatePath)
        {
            // Keep all naming-path composition on the shared helper so filesystem roots
            // and Unix/Docker whitespace semantics stay consistent with import/move flows.
            return FileUtils.CombineWithOptionalBase(basePath, candidatePath);
        }
    }
}
