/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library;

/// <summary>
/// The operator's half of the not-found flag: a scan only ever marks a file it cannot
/// find, so removing the row is a deliberate act here. Nothing on disk is touched — the
/// file is not there, which is the point.
/// </summary>
public sealed class LibraryNotFoundFilesWorkflow(
    IAudiobookRepository audiobookRepository,
    IAudiobookFileRepository audiobookFileRepository,
    IHistoryRepository historyRepository,
    IAudiobookUpdatePublisher updatePublisher,
    ILogger<LibraryNotFoundFilesWorkflow> logger)
{
    public async Task<IActionResult> RemoveAsync(int audiobookId, CancellationToken cancellationToken)
    {
        var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
        if (audiobook == null)
        {
            return new NotFoundObjectResult(new { message = "Audiobook not found." });
        }

        var removed = await audiobookFileRepository.DeleteNotFoundAsync(audiobookId, cancellationToken);
        if (removed.Count == 0)
        {
            return new OkObjectResult(new { audiobookId, removed = 0, files = Array.Empty<object>() });
        }

        // The legacy single-file path may name a row just removed; a book with no files
        // left should read as having none.
        var remaining = (await audiobookFileRepository.GetByAudiobookIdAsync(audiobookId, cancellationToken)).Count;
        if (remaining == 0 && !string.IsNullOrWhiteSpace(audiobook.FilePath))
        {
            audiobook.FilePath = null;
            audiobook.FileSize = null;
            await audiobookRepository.UpdateAsync(audiobook);
        }

        foreach (var file in removed)
        {
            await historyRepository.AddAsync(new History
            {
                AudiobookId = audiobookId,
                AudiobookTitle = audiobook.Title ?? "Unknown",
                EventType = "File Removed",
                Message = $"Not-found file removed from the library: {Path.GetFileName(file.Path)}",
                Source = "operator",
                Data = JsonSerializer.Serialize(new { file.Path }),
                Timestamp = DateTime.UtcNow
            });
        }

        logger.LogInformation(
            "Removed {Count} not-found file row(s) from audiobook {AudiobookId}",
            removed.Count,
            audiobookId);

        var payload = removed.Select(file => new { id = file.Id, path = file.Path }).ToList();
        try
        {
            // Every open page of this book re-reads it; the rows are simply gone.
            await updatePublisher.PublishCurrentAsync(audiobookId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not broadcast the removal of not-found files for audiobook {AudiobookId}", audiobookId);
        }

        return new OkObjectResult(new { audiobookId, removed = removed.Count, files = payload });
    }
}
