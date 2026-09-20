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
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Domain.Common;

namespace Listenarr.Api.Features.Library
{
    /// <summary>
    /// The folder a record is created with under a root: the naming pattern applied
    /// to the book's metadata. One answer for the Add modal's path preview and for a
    /// found book's import, so both record the same base path. Falls back to the
    /// root when the pattern cannot be applied, so an add is never blocked on it.
    /// </summary>
    public sealed class LibraryDestinationPlanner(
        IConfigurationService configurationService,
        IFileNamingService fileNamingService,
        ILogger<LibraryDestinationPlanner> logger) : ILibraryDestinationPlanner
    {
        public async Task<LibraryDestinationPlan> PlanBookFolderAsync(AudibleBookMetadata metadata, string rootPath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            try
            {
                var settings = await configurationService.GetApplicationSettingsAsync();
                var audiobook = metadata.ToAudiobook();
                AudiobookSeriesMembershipHelper.ApplyToAudiobook(
                    audiobook,
                    metadata.SeriesMemberships,
                    metadata.Series,
                    metadata.SeriesNumber);

                var namingPattern = !string.IsNullOrWhiteSpace(settings.FolderNamingPattern)
                    ? settings.FolderNamingPattern
                    : settings.FileNamingPattern;
                var relativePath = LibraryPathPlanner.ComputeAudiobookRelativeDirectoryFromPattern(
                    audiobook,
                    namingPattern,
                    fileNamingService);
                var planned = FileUtils.CombineWithOptionalBase(rootPath, relativePath);
                return string.IsNullOrWhiteSpace(planned)
                    ? new LibraryDestinationPlan(rootPath, string.Empty)
                    : new LibraryDestinationPlan(planned, relativePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
            {
                logger.LogWarning(ex, "Could not plan a folder for {Asin} under {Root}; using the root", metadata.Asin, rootPath);
                return new LibraryDestinationPlan(rootPath, string.Empty);
            }
        }
    }
}
