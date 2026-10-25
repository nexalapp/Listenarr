/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Application.Downloads.Submission;

/// <summary>Metadata keys a download carries for every protocol.</summary>
public static class DownloadMetadataKeys
{
    /// <summary>
    /// The archive password the release was grabbed with, protected by ISecretProtector.
    /// Kept because a finished download can be an NZB of the real release, whose archives
    /// need the same password.
    /// </summary>
    public const string ProtectedArchivePassword = "ProtectedArchivePassword";

    /// <summary>The downloads an NZB found inside this one was submitted as.</summary>
    public const string ContinuedAs = "ContinuedAs";

    /// <summary>The download whose finished files were an NZB, for the one it became.</summary>
    public const string ContinuesFrom = "ContinuesFrom";
}
