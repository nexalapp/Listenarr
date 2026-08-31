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
using System.Text.RegularExpressions;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Common
{
    public partial class FileNamingService : IFileNamingService
    {
        private static readonly HashSet<char> PortableInvalidFileNameChars = BuildPortableInvalidFileNameChars();
        private static readonly HashSet<string> ReservedWindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "COM¹", "COM²", "COM³",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            "LPT¹", "LPT²", "LPT³"
        };

        private readonly IConfigurationService _configService;
        private readonly ILogger<FileNamingService> _logger;
        private readonly IFileSystemSemanticsResolver? _semanticsResolver;

        public FileNamingService(
            IConfigurationService configService,
            ILogger<FileNamingService> logger,
            IFileSystemSemanticsResolver? semanticsResolver = null)
        {
            _configService = configService;
            _logger = logger;
            _semanticsResolver = semanticsResolver;
        }

        /// <summary>
        /// Apply the configured file naming pattern to generate the output path from settings
        /// </summary>
        public async Task<string> GenerateFilePathAsync(
            AudioMetadata metadata,
            string originalExtension = ".m4b")
        {
            var settings = await _configService.GetApplicationSettingsAsync() ?? new ApplicationSettings();
            return await GenerateFilePathAsync(metadata, settings.OutputPath, originalExtension);
        }

        public async Task<string> GenerateFilePathAsync(
            AudioMetadata metadata,
            string outputPath,
            string originalExtension = ".m4b")
        {
            var settings = await _configService.GetApplicationSettingsAsync() ?? new ApplicationSettings();
            var folderPattern = settings.FolderNamingPattern;

            // Determine if this is a multi-file import (has disk or chapter number)
            bool isMultiFile = metadata.DiscNumber.HasValue || metadata.TrackNumber.HasValue;
            var filePattern = isMultiFile
                ? settings.MultiFileNamingPattern
                : settings.FileNamingPattern;

            var effectiveFolderPattern = folderPattern;
            try
            {
                if (!string.IsNullOrWhiteSpace(outputPath) && !string.IsNullOrWhiteSpace(settings.OutputPath))
                {
                    var requestedIsHostPath = FileSystemPathIdentity.TryCanonicalizeUnambiguousStoredAbsolutePathForHost(
                        outputPath,
                        out var requestedRoot,
                        out _);
                    var configuredIsHostPath = FileSystemPathIdentity.TryCanonicalizeUnambiguousStoredAbsolutePathForHost(
                        settings.OutputPath,
                        out var configuredRoot,
                        out _);
                    if (requestedIsHostPath
                        && (!configuredIsHostPath
                            || !await AreEquivalentOutputRootsAsync(requestedRoot, configuredRoot)))
                    {
                        // Caller provided a custom base path (e.g., audiobook BasePath) -> skip folder pattern.
                        // A configured root from another host cannot authorize the current native path.
                        effectiveFolderPattern = string.Empty;
                    }
                }
            }
            catch (Exception caughtEx_2) when (caughtEx_2 is not OperationCanceledException && caughtEx_2 is not OutOfMemoryException && caughtEx_2 is not StackOverflowException)
            {
                // If paths are invalid, fall back to configured folder pattern
                System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
            }

            var variables = BuildVariables(metadata);

            // Diagnostic logging: record the variables used for pattern replacement
            try
            {
                var dbg = string.Join(", ", variables.Select(kv => $"{kv.Key}='{kv.Value}'"));
                _logger.LogInformation("FileNamingService variables: {Vars}", dbg);
            }
            catch (Exception caughtEx_3) when (caughtEx_3 is not OperationCanceledException && caughtEx_3 is not OutOfMemoryException && caughtEx_3 is not StackOverflowException)
            {
                // ignore logging errors
                System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
            }

            string relativePath;
            if (string.IsNullOrWhiteSpace(effectiveFolderPattern))
            {
                // Legacy behavior: use FileNamingPattern as the full relative path pattern
                var legacyPattern = string.IsNullOrWhiteSpace(filePattern)
                    ? "{Author}/{Series}/{Title}"
                    : filePattern;

                relativePath = ApplyNamingPattern(legacyPattern, variables);
            }
            else
            {
                // New behavior: separate folder and file patterns
                var effectiveFilePattern = string.IsNullOrWhiteSpace(filePattern) ? "{Title}" : filePattern;

                var folderRelative = ApplyNamingPattern(effectiveFolderPattern, variables, treatAsFilename: false);

                // Normalize path separators to platform-specific ones
                if (!string.IsNullOrWhiteSpace(folderRelative))
                {
                    folderRelative = folderRelative.Replace('/', Path.DirectorySeparatorChar)
                                                   .Replace('\\', Path.DirectorySeparatorChar);
                }

                var patternAllowsSubfolders = effectiveFilePattern.IndexOf("DiskNumber", StringComparison.OrdinalIgnoreCase) >= 0
                    || effectiveFilePattern.IndexOf("ChapterNumber", StringComparison.OrdinalIgnoreCase) >= 0
                    || effectiveFilePattern.IndexOf('/') >= 0
                    || effectiveFilePattern.IndexOf('\\') >= 0;

                var fileRelative = ApplyNamingPattern(effectiveFilePattern, variables, treatAsFilename: !patternAllowsSubfolders);

                relativePath = string.IsNullOrWhiteSpace(folderRelative)
                    ? fileRelative
                    : CombineWithOptionalBase(folderRelative, fileRelative);
            }

            // Ensure it has the correct extension
            if (!relativePath.EndsWith(originalExtension, StringComparison.OrdinalIgnoreCase))
            {
                relativePath += originalExtension;
            }

            // Combine with the provided output path
            var fullPath = string.IsNullOrWhiteSpace(outputPath)
                ? relativePath
                : CombineWithOptionalBase(outputPath, relativePath);

            fullPath = EnsurePathWithinLimits(fullPath);

            _logger.LogInformation("Generated file path: {FilePath}", fullPath);
            return fullPath;
        }

        private async Task<bool> AreEquivalentOutputRootsAsync(string requestedRoot, string configuredRoot)
        {
            if (_semanticsResolver != null)
            {
                var resolution = await _semanticsResolver.ResolveAsync(
                    configuredRoot,
                    FileSystemCaseSensitivityMode.Auto);
                if (resolution.State == PathIdentityState.Valid)
                {
                    return FileSystemPathIdentity.AreEquivalent(
                        requestedRoot,
                        configuredRoot,
                        resolution.Semantics);
                }
            }

            return string.Equals(
                FileSystemPathIdentity.Canonicalize(requestedRoot, GetNativePathSyntax()),
                FileSystemPathIdentity.Canonicalize(configuredRoot, GetNativePathSyntax()),
                StringComparison.Ordinal);
        }

        private static FileSystemPathSyntax GetNativePathSyntax() =>
            OperatingSystem.IsWindows() ? FileSystemPathSyntax.Windows : FileSystemPathSyntax.Unix;

        /// <summary>
        /// How a rendered pattern is finished off.
        ///
        /// The token substitution and the empty-group collapse are identical in all
        /// three; what differs is what the result is allowed to contain. A path may not
        /// hold a colon and is split on slashes; a tag may hold both, and a blurb keeps
        /// the paragraph breaks that a name would have flattened.
        /// </summary>
        private enum NamingRenderMode
        {
            Path,
            Filename,
            Tag
        }

        // {VariableName} or {VariableName:Format}
        private static readonly Regex VariableRegex =
            new(@"\{(\w+)(?::([^}]+))?\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Render one tag's value from its configured pattern.
        ///
        /// <para>
        /// The same template language as the naming patterns, deliberately: the album tag
        /// has to mirror the folder name, and the empty-token collapse is what lets one
        /// pattern produce "[The Expanse 2.7] Drive" for a series book and "Drive" for a
        /// standalone with no conditional anywhere.
        /// </para>
        /// <para>
        /// Returns empty when the pattern holds tokens and every one of them resolved
        /// empty. That is what stops "https://www.audible.com/pd/{Asin}" from writing a
        /// bare URL prefix into a book that has no ASIN — a tag with only its scaffolding
        /// left is worse than no tag.
        /// </para>
        /// </summary>
        public string RenderTagValue(string pattern, AudioMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return string.Empty;
            }

            var variables = BuildVariables(metadata, sanitizeForPath: false);

            // A pattern that is nothing but one token renders that token verbatim. The
            // cleanup below exists to tidy the gaps a collapsed token leaves between
            // literals, and a blurb has no such gaps -- running it would flatten the
            // paragraphs the description atom is read for.
            var lone = VariableRegex.Match(pattern.Trim());
            if (lone.Success && lone.Length == pattern.Trim().Length)
            {
                return variables.TryGetValue(lone.Groups[1].Value, out var soleValue)
                    ? FormatValue(soleValue, lone.Groups[2].Success ? lone.Groups[2].Value : null).Trim()
                    : string.Empty;
            }

            return ApplyNamingPattern(pattern, variables, NamingRenderMode.Tag);
        }

        public string ApplyNamingPattern(string pattern, Dictionary<string, object> variables, bool treatAsFilename = false) =>
            ApplyNamingPattern(
                pattern,
                variables,
                treatAsFilename ? NamingRenderMode.Filename : NamingRenderMode.Path);

        private string ApplyNamingPattern(string pattern, Dictionary<string, object> variables, NamingRenderMode mode)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return "";
            }

            var result = pattern;
            var treatAsFilename = mode == NamingRenderMode.Filename;
            var variableRegex = VariableRegex;

            // A pattern whose every token came back empty has nothing of the book left in
            // it, only the literals that were meant to frame them.
            var tokensSeen = 0;
            var tokensResolved = 0;

            // Replace variables. If a variable is empty, emit a sentinel so we can clean up surrounding
            // punctuation and separators (for example: remove "{Series}/" when Series is empty).
            const string EmptySentinel = "\uE000";
            result = variableRegex.Replace(result, match =>
            {
                tokensSeen++;
                var variableName = match.Groups[1].Value;
                var format = match.Groups[2].Success ? match.Groups[2].Value : null;

                if (variables.TryGetValue(variableName, out var value))
                {
                    // Handle empty values
                    if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                    {
                        return EmptySentinel;
                    }

                    tokensResolved++;
                    var renderedValue = FormatValue(value, format);

                    // The variables were already sanitised (or deliberately not) when they
                    // were built, so a tag keeps its punctuation while a path component
                    // has had it replaced.
                    return mode == NamingRenderMode.Tag
                        ? renderedValue
                        : SanitizePathComponent(renderedValue);
                }

                // Variable not found, return sentinel so we can optionally remove surrounding chars
                _logger.LogWarning("Variable {VariableName} not found in naming pattern", variableName);
                return EmptySentinel;
            });

            // Cleanup order matters. Bracket groups are resolved first so a sentinel can never leak a
            // stray separator or slash into the rendered name.

            // Bracket groups: a group whose contents are entirely empty tokens disappears, however many
            // tokens it held; a group that still holds real content keeps the group and drops only the
            // empty tokens. This is what lets "[{Series} {SeriesNumber}]" vanish for a standalone book
            // and render as "[Radicalized]" for a series with no number.
            result = CollapseBracketGroup(result, '(', ')', EmptySentinel);
            result = CollapseBracketGroup(result, '[', ']', EmptySentinel);
            result = CollapseBracketGroup(result, '{', '}', EmptySentinel);

            if (mode == NamingRenderMode.Tag)
            {
                // Horizontal whitespace only, and a comma joins the separator set. A tag
                // value may legitimately span lines -- a blurb has paragraphs -- so the
                // path rules, which treat every kind of whitespace alike and rewrite
                // sentinels into slashes, would corrupt it.
                result = Regex.Replace(result, @"[^\S\r\n]*[-–—:_,][^\S\r\n]*" + EmptySentinel, string.Empty);
                result = Regex.Replace(result, EmptySentinel + @"[^\S\r\n]*[-–—:_,][^\S\r\n]*", string.Empty);
                result = result.Replace(EmptySentinel, string.Empty);
                result = Regex.Replace(result, @"[^\S\r\n]{2,}", " ");
                result = result.Trim();

                // Every token came back empty, so what is left is the pattern's own
                // scaffolding: "https://www.audible.com/pd/" for a book with no ASIN.
                // Writing that is worse than writing nothing.
                return tokensSeen > 0 && tokensResolved == 0 ? string.Empty : result;
            }

            // Remove common separators adjacent to any surviving sentinel (e.g. " - {sentinel}")
            result = Regex.Replace(result, @"\s*[-–—:_]\s*" + EmptySentinel, string.Empty);
            result = Regex.Replace(result, EmptySentinel + @"\s*[-–—:_]\s*", string.Empty);

            // Remove sentinel next to slashes
            result = Regex.Replace(result, @"/?" + EmptySentinel + @"/?", "/");

            // Finally remove any remaining sentinels
            result = result.Replace(EmptySentinel, string.Empty);

            // Clean up multiple consecutive slashes or spaces
            result = Regex.Replace(result, @"[\\/]{2,}", "/");
            result = Regex.Replace(result, @"\s{2,}", " ");

            if (treatAsFilename)
            {
                // If we're generating a filename (not a path), ensure no directory separators remain.
                // Split on any slashes and take the last segment to avoid creating directories from tokens.
                var partsForFilename = result.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                result = partsForFilename.Length > 0 ? partsForFilename.Last().Trim() : result.Trim();

                // Remove any stray separators and sanitize the filename component
                result = result.Replace("/", string.Empty).Replace("\\", string.Empty);
                result = SanitizePathComponent(result);
            }
            else
            {
                // Remove leading/trailing slashes and spaces from each path component
                var parts = result.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                // Collapse adjacent duplicate components (case-insensitive) to avoid
                // patterns producing repeated folders like "Title/Title (...)/Title"
                for (int i = parts.Count - 1; i > 0; i--)
                {
                    if (string.Equals(parts[i], parts[i - 1], StringComparison.OrdinalIgnoreCase))
                    {
                        parts.RemoveAt(i);
                    }
                }

                // Sanitize each path component to remove invalid characters
                var sanitizedParts = parts.Select(p => SanitizePathComponent(p)).ToList();
                result = string.Join(Path.DirectorySeparatorChar.ToString(), sanitizedParts);
            }

            return result;
        }

        /// <summary>
        /// Render one resolved variable, applying a numeric format such as
        /// <c>{DiskNumber:00}</c> when the value can carry one.
        /// </summary>
        private static string FormatValue(object? value, string? format)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(format))
            {
                return value.ToString() ?? string.Empty;
            }

            if (value is int intValue)
            {
                return intValue.ToString(format);
            }

            return int.TryParse(value.ToString(), out var parsedInt)
                ? parsedInt.ToString(format)
                : value.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Resolves a single kind of bracket group against empty-token sentinels.
        /// Groups holding nothing but sentinels are removed entirely; groups that retain real content
        /// keep their brackets with the sentinels stripped out.
        /// </summary>
        private static string CollapseBracketGroup(string input, char open, char close, string sentinel)
        {
            // Escape explicitly rather than via Regex.Escape: Regex.Escape leaves ']' and '}' alone,
            // and a bare ']' would close the negated character class below early.
            var openEscaped = "\\" + open;
            var closeEscaped = "\\" + close;

            // Non-nested groups only - a group may not contain its own delimiters.
            var groupPattern = openEscaped + "[^" + openEscaped + closeEscaped + "]*" + closeEscaped;

            return Regex.Replace(input, groupPattern, match =>
            {
                var group = match.Value;
                if (!group.Contains(sentinel, StringComparison.Ordinal))
                {
                    return group;
                }

                var inner = group
                    .Substring(1, group.Length - 2)
                    .Replace(sentinel, string.Empty, StringComparison.Ordinal);

                inner = Regex.Replace(inner, @"\s{2,}", " ").Trim();

                // Nothing meaningful survived - drop the whole group.
                if (!inner.Any(char.IsLetterOrDigit))
                {
                    return string.Empty;
                }

                return open + inner + close;
            });
        }

        public string ApplyNamingPattern(string pattern, AudioMetadata metadata, bool treatAsFilename = false)
        {
            var variables = BuildVariables(metadata);
            return ApplyNamingPattern(pattern, variables, treatAsFilename);
        }

        public string ApplyNamingPattern(string pattern, AudibleBookMetadata metadata, bool treatAsFilename = false)
        {
            var variables = BuildVariables(metadata);
            return ApplyNamingPattern(pattern, variables, treatAsFilename);
        }
    }
}
