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
        private string BuildExpectedPath(Audiobook audiobook, PreviewFileEntry file, ApplicationSettings settings, string basePath, bool isCustomBasePath, bool isMultiFile, int seriesPositionWidth)
        {
            var folderPattern = settings.FolderNamingPattern;
            var filePattern = isMultiFile ? settings.MultiFileNamingPattern : settings.FileNamingPattern;
            var variables = BuildNamingVariables(audiobook, folderPattern, filePattern, file.SequenceNumber, isMultiFile, seriesPositionWidth);
            // The same rendering the tag planner applies, so folder and album agree.
            variables["Series"] = _fileNamingService.RenderSeriesName(audiobook.Series);
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

        private Dictionary<string, object> BuildNamingVariables(Audiobook audiobook, string? folderPattern, string? filePattern, int sequenceNumber, bool isMultiFile, int seriesPositionWidth)
        {
            var combinedTitle = audiobook.Title;
            var authors = audiobook.Authors?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [];
            // Author and narrator go through the naming service's renderers so a rename
            // agrees with a tag write and a conversion about who a book files under and
            // how many narrators a name carries.
            var narrator = _fileNamingService.RenderNarrators(
                audiobook.Narrators != null ? string.Join(", ", audiobook.Narrators.Where(n => !string.IsNullOrWhiteSpace(n))) : string.Empty);

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "Author", _fileNamingService.RenderAuthor(audiobook.Series, authors) },
                { "Authors", authors.Count > 0 ? string.Join(", ", authors) : "Unknown Author" },
                { "Series", audiobook.Series ?? string.Empty },
                { "Title", string.IsNullOrWhiteSpace(combinedTitle) ? "Unknown Title" : combinedTitle },
                { "Subtitle", audiobook.Subtitle ?? string.Empty },
                { "Edition", audiobook.Edition ?? string.Empty },
                { "Narrator", narrator },
                { "Publisher", audiobook.Publisher ?? string.Empty },
                { "Language", audiobook.Language ?? string.Empty },
                { "Asin", audiobook.Asin ?? string.Empty },
                // Widened to the series' own longest position so a folder listing sorts
                // book 2 before book 10. A series that never reaches ten is unchanged.
                { "SeriesNumber", SeriesNumberFormatting.Pad(
                    audiobook.SeriesNumber,
                    seriesPositionWidth) ?? string.Empty },
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
