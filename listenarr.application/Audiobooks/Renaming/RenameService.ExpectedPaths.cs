/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Common;

namespace Listenarr.Application.Audiobooks.Renaming
{
    /// <summary>
    /// Where the naming patterns say a file belongs. Separated from the rest of the
    /// helpers because this is the only part of organizing that renders a pattern, and it
    /// is the part that decides what the Tags table paints yellow.
    /// </summary>
    public partial class RenameService
    {
        private string BuildExpectedPath(Audiobook audiobook, PreviewFileEntry file, ApplicationSettings settings, string basePath, bool isCustomBasePath, bool isMultiFile)
        {
            var folderPattern = settings.FolderNamingPattern;
            var filePattern = isMultiFile ? settings.MultiFileNamingPattern : settings.FileNamingPattern;
            var variables = BuildNamingVariables(audiobook, folderPattern, filePattern, file.SequenceNumber, isMultiFile);
            var patternHasNumberTokens = !string.IsNullOrWhiteSpace(filePattern)
                && (filePattern.IndexOf("DiskNumber", StringComparison.OrdinalIgnoreCase) >= 0 || filePattern.IndexOf("ChapterNumber", StringComparison.OrdinalIgnoreCase) >= 0);

            string relativePath;
            if (string.IsNullOrWhiteSpace(folderPattern))
            {
                var legacyPattern = string.IsNullOrWhiteSpace(filePattern) ? "{Author}/{Title}/{Title}" : filePattern;
                relativePath = _fileNamingService.ApplyNamingPattern(legacyPattern, variables, false);
            }
            else if (isCustomBasePath)
            {
                var effectiveFilePattern = string.IsNullOrWhiteSpace(filePattern) ? "{Title}" : filePattern;
                relativePath = _fileNamingService.ApplyNamingPattern(effectiveFilePattern, variables, !PatternAllowsSubfolders(effectiveFilePattern));
            }
            else
            {
                var effectiveFilePattern = string.IsNullOrWhiteSpace(filePattern) ? "{Title}" : filePattern;
                var folderRelative = _fileNamingService.ApplyNamingPattern(folderPattern, variables, false);
                var fileRelative = _fileNamingService.ApplyNamingPattern(effectiveFilePattern, variables, !PatternAllowsSubfolders(effectiveFilePattern));
                if (isMultiFile && !patternHasNumberTokens) fileRelative = FileUtils.AppendSequenceSuffix(fileRelative, file.SequenceNumber);
                relativePath = string.IsNullOrWhiteSpace(folderRelative) ? fileRelative : CombineWithOptionalBase(folderRelative, fileRelative);
            }

            if ((string.IsNullOrWhiteSpace(folderPattern) || isCustomBasePath) && isMultiFile && !patternHasNumberTokens)
                relativePath = FileUtils.AppendSequenceSuffix(relativePath, file.SequenceNumber);
            if (!relativePath.EndsWith(file.Extension, StringComparison.OrdinalIgnoreCase)) relativePath += file.Extension;

            return string.IsNullOrWhiteSpace(basePath) ? NormalizePath(relativePath) : NormalizePath(CombineWithOptionalBase(basePath, relativePath));
        }

        /// <summary>
        /// Whether folding the subtitle into <c>{Title}</c> would tell the name anything
        /// it is not already going to say.
        /// </summary>
        /// <remarks>
        /// A title alone is often ambiguous, so a subtitle is folded in when the pattern
        /// gives it nowhere else to go. The cases below are the ones where doing that
        /// states the same fact twice:
        /// <list type="bullet">
        /// <item>
        /// The pattern uses <c>{Subtitle}</c> itself, so the subtitle already has a place.
        /// </item>
        /// <item>
        /// Either of the two contains the other. Audible files a series book's subtitle as
        /// the title restated — "Dogs of War, Book 1" against a title of "Dogs of War" —
        /// and only the reverse of that was caught before, so the pattern produced
        /// "Dogs of War - Dogs of War, Book 1".
        /// </item>
        /// <item>
        /// The pattern renders <c>{Series}</c> and the subtitle is that series named
        /// again: "[Revelation Space 10] Dilation Sleep - Revelation Space, Book #10"
        /// says the series twice and the position twice.
        /// </item>
        /// </list>
        /// </remarks>
        private static bool SubtitleAddsSomething(
            Audiobook audiobook,
            string? folderPattern,
            string? filePattern)
        {
            var title = audiobook.Title;
            var subtitle = audiobook.Subtitle;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(subtitle))
            {
                return false;
            }

            if (PatternUses("Subtitle"))
            {
                return false;
            }

            if (title.Contains(subtitle, StringComparison.OrdinalIgnoreCase)
                || subtitle.Contains(title, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var series = audiobook.Series;
            if (!string.IsNullOrWhiteSpace(series)
                && PatternUses("Series")
                && subtitle.Contains(series, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;

            bool PatternUses(string token) =>
                (!string.IsNullOrWhiteSpace(folderPattern)
                    && folderPattern.Contains(token, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(filePattern)
                    && filePattern.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, object> BuildNamingVariables(Audiobook audiobook, string? folderPattern, string? filePattern, int sequenceNumber, bool isMultiFile)
        {
            var combinedTitle = SubtitleAddsSomething(audiobook, folderPattern, filePattern)
                ? $"{audiobook.Title}: {audiobook.Subtitle}"
                : audiobook.Title;
            var narrator = audiobook.Narrators != null ? string.Join(", ", audiobook.Narrators.Where(n => !string.IsNullOrWhiteSpace(n))) : string.Empty;

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "Author", audiobook.Authors?.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Unknown Author" },
                { "Series", audiobook.Series ?? string.Empty },
                { "Title", string.IsNullOrWhiteSpace(combinedTitle) ? "Unknown Title" : combinedTitle },
                { "Subtitle", audiobook.Subtitle ?? string.Empty },
                { "Edition", audiobook.Edition ?? string.Empty },
                { "Narrator", narrator },
                { "Publisher", audiobook.Publisher ?? string.Empty },
                { "Language", audiobook.Language ?? string.Empty },
                { "Asin", audiobook.Asin ?? string.Empty },
                { "SeriesNumber", audiobook.SeriesNumber ?? string.Empty },
                { "Year", audiobook.PublishYear ?? string.Empty },
                { "Quality", audiobook.Quality ?? string.Empty },
                { "DiskNumber", isMultiFile ? sequenceNumber : string.Empty },
                { "ChapterNumber", isMultiFile ? sequenceNumber : string.Empty }
            };
        }

        private static bool PatternAllowsSubfolders(string pattern)
            => pattern.IndexOf("DiskNumber", StringComparison.OrdinalIgnoreCase) >= 0
                || pattern.IndexOf("ChapterNumber", StringComparison.OrdinalIgnoreCase) >= 0
                || pattern.IndexOf('/') >= 0
                || pattern.IndexOf('\\') >= 0;

        private sealed record PreviewFileEntry(
            int FileId,
            string CurrentPath,
            string Extension,
            int SequenceNumber,
            bool PathLocked);
    }
}
