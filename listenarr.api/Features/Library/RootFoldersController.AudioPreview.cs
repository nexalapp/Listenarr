/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library;

public partial class RootFoldersController
{
    /// <summary>
    /// Streams an audio file from inside a root folder so the import page can play a
    /// sample of a book before committing to a match.
    ///
    /// <para>
    /// Range processing is enabled because the player needs it: an M4B commonly carries
    /// its moov atom at the end of the file, so a browser seeks there before it can decode
    /// the first second, and without ranges it would have to pull the whole book to do it.
    /// </para>
    /// <para>
    /// The response is the file itself, so the two-minute limit the import page applies is
    /// a convention of that player and not a boundary this endpoint enforces. What it does
    /// enforce is which files it will open at all: an audio file, inside this root folder.
    /// </para>
    /// </summary>
    [HttpGet("{id}/audio-preview")]
    public async Task<IActionResult> GetAudioPreview(int id, [FromQuery] string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(new { message = "A file path is required." });
        }

        var folder = await _service.GetByIdAsync(id);
        if (folder == null)
        {
            return NotFound(new { message = "Root folder not found" });
        }

        var storage = await _storageHealthResolver.ResolveAsync(folder);
        if (!storage.CanReadFilesystem)
        {
            return Conflict(new
            {
                message = storage.Message
                    ?? "The root folder cannot be read in its current storage state.",
                code = "root_folder_read_unavailable"
            });
        }

        FileSystemPathSemantics semantics;
        try
        {
            semantics = await ResolveFolderSemanticsAsync(folder);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message, code = "root_folder_read_unavailable" });
        }

        // The path arrives from the client, so it is only trusted once it canonicalizes to
        // something inside this root folder. Without this the endpoint would hand out the
        // bytes of any file the process can open.
        var canonicalRoot = TryCanonicalizePathForComparison(folder.Path, semantics);
        var canonicalFile = TryCanonicalizePathForComparison(path, semantics);
        if (canonicalRoot == null || canonicalFile == null
            || !IsWithinRoot(canonicalFile, canonicalRoot, semantics))
        {
            return BadRequest(new { message = "The file is not inside this root folder." });
        }

        // Containment alone would still expose a cover image, an NFO or a log sitting beside
        // the book. Only the files the scanner treats as audio are playable.
        if (!FileUtils.IsAudioFile(canonicalFile))
        {
            return BadRequest(new { message = "That file is not an audio file." });
        }

        if (!_fileSystem.FileExists(canonicalFile))
        {
            return NotFound(new { message = "File not found" });
        }

        return new PhysicalFileResult(canonicalFile, AudioContentTypes.ForFile(canonicalFile))
        {
            EnableRangeProcessing = true
        };
    }
}
