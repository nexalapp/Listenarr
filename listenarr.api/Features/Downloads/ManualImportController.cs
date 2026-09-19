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
using Microsoft.AspNetCore.Mvc;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Domain.Common;
using Listenarr.Api.Dtos.ManualImport;

namespace Listenarr.Api.Features.Downloads;

[ApiController]
[Route("api/v{version:apiVersion}/library/manual-import")]
[Tags("Library")]
public sealed class ManualImportController : ControllerBase
{
    private readonly ILogger<ManualImportController> _logger;
    private readonly IConfigurationService _configService;
    private readonly IFileSystem _fileSystem;
    private readonly ManualImportWorkflow _workflow;

    public ManualImportController(
        ILogger<ManualImportController> logger,
        IConfigurationService configService,
        IFileSystem fileSystem,
        ManualImportWorkflow workflow)
    {
        _logger = logger;
        _configService = configService;
        _fileSystem = fileSystem;
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
    }

    /// <summary>
    /// Preview the files available for manual import from a directory.
    /// </summary>
    /// <param name="path">Absolute path to the directory to scan.</param>
    /// <returns>List of files with relative paths, sizes, and tentative metadata.</returns>
    [HttpGet("preview")]
    public async Task<ActionResult<object>> Preview([FromQuery] string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return BadRequest(new { error = "Path is required" });

            var normalized = Path.GetFullPath(path);
            if (!_fileSystem.DirectoryExists(normalized)) return NotFound(new { error = "Directory not found" });

            var settings = await _configService.GetApplicationSettingsAsync();

            var files = _fileSystem.EnumerateFiles(normalized, "*.*", SearchOption.AllDirectories)
                .Where(f => !FileUtils.IsBlacklistedFile(f, settings.ImportBlacklistExtensions))
                .Select(f => new
                {
                    relativePath = Path.GetRelativePath(normalized, f),
                    fullPath = f,
                    size = _fileSystem.GetFileLength(f),
                    // Simple heuristics for sample metadata
                    series = (string?)null,
                    season = (string?)null,
                    episodes = (string?)null,
                    quality = (string?)null,
                    languages = new string[] { "English" },
                    releaseType = "Unknown"
                })
                .ToList();

            var items = files.Select(f => new
            {
                relativePath = f.relativePath,
                fullPath = f.fullPath,
                size = FormatSize(f.size),
                series = f.series,
                season = f.season,
                episodes = f.episodes,
                quality = f.quality,
                languages = f.languages,
                releaseType = f.releaseType
            }).ToList();

            return Ok(new { items });
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error previewing manual import for path {Path}", path);
            return StatusCode(500, new { error = "Failed to preview import" });
        }
    }

    /// <summary>
    /// Given a list of items, tries to import them all into the library
    /// </summary>
    /// <param name="request">Import configuration including source path, mode, import action (do nothing/copy/move/...), and selected file items.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>Summary of imported files with success/failure details per item.</returns>
    [HttpPost]
    public async Task<ActionResult<object>> Start(
        [FromBody] ManualImportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Path))
        {
            return BadRequest(new { error = "Invalid request" });
        }

        if (!_fileSystem.DirectoryExists(Path.GetFullPath(request.Path)))
        {
            return NotFound(new { error = "Directory not found" });
        }

        if (request.Items == null || !request.Items.Any())
        {
            return BadRequest(new { error = "No items to import" });
        }

        try
        {
            var batch = await _workflow.StartAsync(request, cancellationToken);
            return Ok(new
            {
                importedCount = batch.ImportedCount,
                totalCount = batch.TotalCount,
                stoppedByCancellation = batch.StoppedByCancellation,
                results = batch.Results
            });
        }
        catch (ApplicationConflictException exception)
        {
            return Conflict(new
            {
                error = exception.SafeDetail,
                code = exception.Code
            });
        }
        catch (ApplicationUnavailableException)
        {
            // The filesystem gate's own exception carries the 503 the exception
            // handler already knows how to write.
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error starting manual import");
            return StatusCode(500, new { error = "Failed to start import" });
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var units = new[] { "KiB", "MiB", "GiB", "TiB" };
        double size = bytes / 1024.0;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024.0;
            unit++;
        }
        return $"{size:F1} {units[unit]}";
    }
}
