/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Application.FoundBooks.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library;

public sealed class LibraryPreviewPathWorkflow(
    IConfigurationService configurationService,
    IRootFolderService rootFolderService,
    ILibraryDestinationPlanner planner,
    ILogger<LibraryPreviewPathWorkflow> logger)
{
    public async Task<IActionResult> PreviewAsync(LibraryController.PreviewPathRequest request)
    {
        try
        {
            var settings = await configurationService.GetApplicationSettingsAsync();
            var explicitRoot = !string.IsNullOrEmpty(request.DestinationRoot);
            var defaultRoot = explicitRoot
                ? null
                : await rootFolderService.GetDefaultAsync();
            var root = explicitRoot
                ? request.DestinationRoot
                : defaultRoot?.Path ?? settings.OutputPath;

            var plan = await planner.PlanBookFolderAsync(request.Metadata, root ?? string.Empty);

            return new OkObjectResult(new { fullPath = plan.FullPath, relativePath = plan.RelativePath, root });
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            logger.LogWarning(exception, "Failed to compute preview path");
            return new ObjectResult(new { message = "Failed to compute preview path" })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
