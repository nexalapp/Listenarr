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

/// <summary>
/// Turns NZBGet's status word into a reason Listenarr can act on, and the sentence it
/// shows beside it.
///
/// Both come from one classification on purpose: the message a person reads and the
/// decision about blocking and re-searching have to be about the same failure, and when
/// they were derived separately they drifted - a release could be described as damaged
/// and treated as a local problem.
/// </summary>
internal static class NzbgetFailureMessageMapper
{
    /// <summary>
    /// Which failure NZBGet is reporting. Its statuses are "CATEGORY/DETAIL" and the
    /// detail is what matters: HEALTH arrives as FAILURE/HEALTH, WARNING/HEALTH and
    /// DELETED/HEALTH depending on which setting caught it, and all three mean the same
    /// thing about the release.
    /// </summary>
    public static DownloadFailureReason Classify(string? rawStatus)
    {
        var status = (rawStatus ?? string.Empty).Trim();
        if (status.Length == 0)
        {
            return DownloadFailureReason.Unknown;
        }

        if (Is(status, "FAILURE/MOVE") || Is(status, "FAILURE/POSTPROCESS"))
        {
            return DownloadFailureReason.PostProcessing;
        }

        // The password is tested before unpacking, because NZBGet reports a wrong
        // password as a warning while the unpack it belongs to reports as a failure.
        if (Is(status, "WARNING/PASSWORD"))
        {
            return DownloadFailureReason.Password;
        }

        if (Is(status, "FAILURE/HEALTH") || Is(status, "WARNING/HEALTH") || Is(status, "DELETED/HEALTH"))
        {
            return DownloadFailureReason.Health;
        }

        if (Is(status, "FAILURE/PAR") ||
            Is(status, "FAILURE/REPAIR") ||
            Is(status, "FAILURE/UNPACK") ||
            Is(status, "WARNING/DAMAGED") ||
            Is(status, "DELETED/BAD"))
        {
            return DownloadFailureReason.Damaged;
        }

        if (Is(status, "WARNING/SPACE"))
        {
            return DownloadFailureReason.DiskSpace;
        }

        if (Is(status, "DELETED/COPY") || Is(status, "DELETED/DUPE"))
        {
            return DownloadFailureReason.Duplicate;
        }

        return DownloadFailureReason.Unknown;
    }

    public static string Map(NzbgetHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var status = entry.RawStatus.Trim();
        return Describe(Classify(status), status);
    }

    /// <summary>
    /// The sentence shown beside the reason. An unrecognised status is quoted rather than
    /// paraphrased: NZBGet's own word is more use than a guess at what it meant.
    /// </summary>
    public static string Describe(DownloadFailureReason reason, string? rawStatus)
    {
        var status = (rawStatus ?? string.Empty).Trim();
        return reason switch
        {
            DownloadFailureReason.PostProcessing =>
                "NZBGet failed during post-processing or final move. Check the NZBGet completed folder for existing files, permissions, or path conflicts.",
            DownloadFailureReason.Password =>
                "NZBGet could not unpack the download because the archive password was wrong. The NZB may not be the release the password belongs to.",
            DownloadFailureReason.Health =>
                "NZBGet marked the download failed because its health dropped below the required threshold.",
            DownloadFailureReason.Damaged when Is(status, "FAILURE/UNPACK") =>
                "NZBGet failed while unpacking the download. The archive may be damaged, incomplete, password-protected, or missing parts.",
            DownloadFailureReason.Damaged when Is(status, "WARNING/DAMAGED") || Is(status, "DELETED/BAD") =>
                "NZBGet finished the download but its files are damaged and could not be repaired.",
            DownloadFailureReason.Damaged =>
                "NZBGet failed while verifying or repairing the download. The release may be incomplete or damaged.",
            DownloadFailureReason.DiskSpace =>
                "NZBGet ran out of disk space before the download could be unpacked.",
            // Usually the same NZB grabbed again after the first copy failed, so the
            // first copy's failure is the one to look at.
            DownloadFailureReason.Duplicate =>
                "NZBGet discarded the download as a duplicate of one already in its history, so nothing new was fetched.",
            _ => status.Length == 0
                ? "NZBGet reported a failed download."
                : $"NZBGet reported a failed download: {status}"
        };
    }

    private static bool Is(string status, string prefix) =>
        status.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
