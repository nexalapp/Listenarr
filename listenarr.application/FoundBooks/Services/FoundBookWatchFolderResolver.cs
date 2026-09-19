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
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.FoundBooks.Services
{
    public sealed class FoundBookWatchFolderResolver(
        IConfigurationService configurationService,
        IRemotePathMappingService remotePathMappingService,
        IRootFolderRepository rootFolderRepository,
        IFileSystem fileSystem,
        IFileSystemSemanticsResolver semanticsResolver,
        ILogger<FoundBookWatchFolderResolver> logger) : IFoundBookWatchFolderResolver
    {
        public async Task<FoundBookWatchFolders> ResolveAsync(CancellationToken cancellationToken = default)
        {
            var settings = await configurationService.GetApplicationSettingsAsync();
            var warnings = new List<string>();
            var configured = settings.FoundBooksWatchFolders?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .ToList() ?? [];

            List<string> candidates;
            var fromSettings = configured.Count > 0;
            if (fromSettings)
            {
                candidates = configured;
            }
            else
            {
                candidates = [];
                var clients = await configurationService.GetDownloadClientConfigurationsAsync();
                foreach (var client in clients.Where(c => c.IsEnabled && !string.IsNullOrWhiteSpace(c.DownloadPath)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        candidates.Add(await remotePathMappingService.TranslatePathAsync(client, client.DownloadPath));
                    }
                    catch (InvalidOperationException ex)
                    {
                        // A mapping whose local side is unavailable on this host is a
                        // configuration problem for the download client too; say so
                        // rather than scanning the remote spelling as if it were local.
                        warnings.Add($"{client.Name}: {ex.Message}");
                        logger.LogWarning(ex, "Could not translate the download path for client {Client}", client.Name);
                    }
                }
            }

            var roots = await rootFolderRepository.GetAllAsync();
            var folders = new List<FoundBookWatchFolder>();
            var unavailable = new List<string>();
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!fileSystem.DirectoryExists(candidate))
                {
                    warnings.Add($"Watch folder does not exist: {candidate}");
                    unavailable.Add(candidate);
                    continue;
                }

                var resolution = await semanticsResolver.ResolveAsync(
                    candidate,
                    FileSystemCaseSensitivityMode.Auto,
                    cancellationToken);
                if (resolution.State != PathIdentityState.Valid)
                {
                    warnings.Add($"Watch folder skipped, its filesystem could not be characterised: {candidate}"
                        + (string.IsNullOrWhiteSpace(resolution.Reason) ? string.Empty : $" ({resolution.Reason})"));
                    unavailable.Add(candidate);
                    continue;
                }

                var semantics = resolution.Semantics;
                var full = FileSystemPathIdentity.Canonicalize(resolution.CanonicalPath ?? candidate, semantics.Syntax);
                var overlapping = roots.FirstOrDefault(root =>
                    FileSystemPathIdentity.IsSameOrInside(full, root.Path, semantics)
                    || FileSystemPathIdentity.IsSameOrInside(root.Path, full, semantics));
                if (overlapping != null)
                {
                    // The library is what found books are added *to*; scanning it as a
                    // source would offer every book back to itself.
                    warnings.Add($"Watch folder overlaps the library root {overlapping.Path}: {full}");
                    continue;
                }

                if (!folders.Any(f => f.Semantics.Comparer.Equals(f.Path, full)))
                {
                    folders.Add(new FoundBookWatchFolder(full, semantics));
                }
            }

            return new FoundBookWatchFolders(folders, unavailable, warnings, fromSettings);
        }
    }
}
