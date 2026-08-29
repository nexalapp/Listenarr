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
    /// Reads the metadata an audiobook file carries inside it — title, author, narrator,
    /// series, description and cover art — for a book no metadata provider can match.
    /// The response is the same shape add-to-library accepts, so the caller can present
    /// it for editing and submit it unchanged.
    /// </summary>
    [HttpPost("{id}/embedded-metadata")]
    public async Task<IActionResult> GetEmbeddedMetadata(
        int id,
        [FromBody] EmbeddedMetadataRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Path))
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

        // The path arrives from the client, so it is only trusted once it canonicalizes
        // to something inside this root folder. Without this a caller could read tags and
        // cover art out of any file the process can open.
        var canonicalRoot = TryCanonicalizePathForComparison(folder.Path, semantics);
        var canonicalFile = TryCanonicalizePathForComparison(request.Path, semantics);
        if (canonicalRoot == null || canonicalFile == null
            || !IsWithinRoot(canonicalFile, canonicalRoot, semantics))
        {
            return BadRequest(new { message = "The file is not inside this root folder." });
        }

        if (!_fileSystem.FileExists(canonicalFile))
        {
            return NotFound(new { message = "File not found" });
        }

        var metadata = await _embeddedFileMetadata.ReadAsync(canonicalFile, cancellationToken);
        if (metadata == null)
        {
            return Conflict(new
            {
                message = "No metadata could be read from this file.",
                code = "embedded_metadata_unavailable"
            });
        }

        return Ok(new { metadata });
    }

    private static bool IsWithinRoot(
        string canonicalFile,
        string canonicalRoot,
        FileSystemPathSemantics semantics)
    {
        var separator = semantics.Syntax == FileSystemPathSyntax.Windows ? '\\' : '/';
        var rootWithSeparator = canonicalRoot.EndsWith(separator)
            ? canonicalRoot
            : canonicalRoot + separator;

        // Comparer carries the root's resolved case sensitivity, so a case-insensitive
        // filesystem cannot be escaped by varying the case of the root prefix.
        return canonicalFile.Length > rootWithSeparator.Length
            && semantics.Comparer.Equals(
                canonicalFile[..rootWithSeparator.Length],
                rootWithSeparator);
    }

    public class EmbeddedMetadataRequest
    {
        public string? Path { get; set; }
    }
}
