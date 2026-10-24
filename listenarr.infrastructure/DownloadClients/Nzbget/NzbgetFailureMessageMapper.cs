/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Infrastructure.DownloadClients.Nzbget;

internal static class NzbgetFailureMessageMapper
{
    public static string Map(NzbgetHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var status = entry.RawStatus.Trim();

        if (status.StartsWith("FAILURE/MOVE", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("FAILURE/POSTPROCESS", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet failed during post-processing or final move. Check the NZBGet completed folder for existing files, permissions, or path conflicts.";
        }

        if (status.StartsWith("FAILURE/UNPACK", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet failed while unpacking the download. The archive may be damaged, incomplete, password-protected, or missing parts.";
        }

        if (status.StartsWith("FAILURE/PAR", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("FAILURE/REPAIR", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet failed while verifying or repairing the download. The release may be incomplete or damaged.";
        }

        if (status.StartsWith("FAILURE/HEALTH", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("WARNING/HEALTH", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("DELETED/HEALTH", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet marked the download failed because its health dropped below the required threshold.";
        }

        if (status.StartsWith("WARNING/PASSWORD", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet could not unpack the download because the archive password was wrong. The NZB may not be the release the password belongs to.";
        }

        if (status.StartsWith("WARNING/DAMAGED", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("DELETED/BAD", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet finished the download but its files are damaged and could not be repaired.";
        }

        if (status.StartsWith("WARNING/SPACE", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet ran out of disk space before the download could be unpacked.";
        }

        if (status.StartsWith("DELETED/COPY", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("DELETED/DUPE", StringComparison.OrdinalIgnoreCase))
        {
            // Usually the same NZB grabbed again after the first copy failed, so the
            // first copy's failure is the one to look at.
            return "NZBGet discarded the download as a duplicate of one already in its history, so nothing new was fetched.";
        }

        return string.IsNullOrWhiteSpace(status)
            ? "NZBGet reported a failed download."
            : $"NZBGet reported a failed download: {status}";
    }

    public static bool IsMoveOrPostProcessingFailure(string? rawStatus)
    {
        var status = (rawStatus ?? string.Empty).Trim();
        return status.StartsWith("FAILURE/MOVE", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("FAILURE/POSTPROCESS", StringComparison.OrdinalIgnoreCase);
    }
}
