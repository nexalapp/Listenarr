using System.Security.Cryptography;
using System.Text;

namespace Listenarr.Infrastructure.FileSystem;

/// <summary>
/// The observation factories the resolver returns: how each way of failing to identify
/// a root folder is turned into a state, a message and a set of capabilities.
/// </summary>
internal sealed partial class RootFolderStorageHealthResolver
{
    private static RootFolderStorageObservation ApplyMutationCapability(
        string canonicalPath,
        RootFolderStorageObservation observation,
        bool? isReadOnly)
    {
        if (isReadOnly == false)
        {
            return observation with
            {
                CanPublishNewFiles = observation.CanMutateFilesystem
            };
        }

        return observation with
        {
            State = RootFolderStorageState.Limited,
            Reason = isReadOnly == true
                ? RootFolderStorageReason.ReadOnlyFilesystem
                : RootFolderStorageReason.MutationCapabilityUnavailable,
            Message = isReadOnly == true
                ? "This storage is mounted read-only. Listenarr can read and scan it, but filesystem mutations are disabled."
                : "Listenarr can read and scan this storage, but it cannot verify that filesystem mutations are available safely.",
            CanConfirmCurrentFolder = false,
            CanMutateFilesystem = false,
            CanPublishNewFiles = false,
            ConfirmationToken = null,
            Detail = isReadOnly == true
                ? "The filesystem reports the ST_RDONLY mount flag."
                : $"The mount access mode could not be determined for {LogRedaction.SanitizeFilePath(canonicalPath)}."
        };
    }

    private static bool? ProbeReadOnlyFileSystem(string canonicalPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            using var boundary = PinnedDirectoryCreation.OpenPinnedBoundary(canonicalPath);
            if (!boundary.VisiblePathMatches())
            {
                return null;
            }

            return boundary.IsLinuxFileSystemReadOnly();
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception
                or InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static RootFolderStorageObservation SemanticsUnavailable(
        RootFolderStorageObservation observation,
        RootFolderStorageReason reason,
        string? detail)
    {
        if (observation.State == RootFolderStorageState.Changed)
        {
            return observation with
            {
                Reason = reason,
                Message = reason == RootFolderStorageReason.FilesystemSemanticsChanged
                    ? "The folder at this location changed and now uses different case-sensitivity rules. Review the root folder settings before using it for filesystem operations."
                    : "The folder at this location changed and Listenarr cannot verify its path rules safely. Review the root folder settings.",
                CanConfirmCurrentFolder = false,
                CanMutateFilesystem = false,
                CanPublishNewFiles = false,
                ConfirmationToken = null,
                Detail = detail
            };
        }

        return Unavailable(reason, detail);
    }

    internal static string CreateConfirmationToken(
        RootFolder root,
        string canonicalPath,
        DirectoryObjectIdentityResolution observedIdentity)
    {
        if (!observedIdentity.IsAvailable)
        {
            throw new InvalidOperationException(
                "A confirmation token requires an available observed directory identity.");
        }

        var material = FormattableString.Invariant(
            $"{ConfirmationTokenVersion}|{root.Id}|{canonicalPath}|{root.DirectoryObjectIdentityVersion?.ToString() ?? "-"}|{root.DirectoryObjectIdentity ?? "-"}|{observedIdentity.Version}|{observedIdentity.Value}");
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
    }

    private static RootFolderStorageObservation LimitedIdentityUnsupported(
        string? detail) =>
        new(
            RootFolderStorageState.Limited,
            RootFolderStorageReason.IdentityUnsupported,
            "This storage can be read and scanned, but it does not expose the durable file identity required for crash-safe moves and deletions.",
            CanConfirmCurrentFolder: false,
            CanChangePath: true,
            CanMutateFilesystem: false,
            ConfirmationToken: null,
            Detail: detail);

    private static RootFolderStorageObservation UnsupportedPersistedIdentity(
        RootFolder root,
        string canonicalPath,
        DirectoryObjectIdentityResolution currentGeneration,
        string? detail) =>
        new(
            RootFolderStorageState.Unconfirmed,
            RootFolderStorageReason.IdentityUnsupported,
            "This folder's saved physical identity version is no longer supported. Review and confirm the current folder before scanning or changing files.",
            CanConfirmCurrentFolder: true,
            CanChangePath: true,
            CanMutateFilesystem: false,
            ConfirmationToken: CreateConfirmationToken(
                root,
                canonicalPath,
                currentGeneration),
            Detail: detail);

    private static RootFolderStorageObservation LimitedLegacyIdentity(
        RootFolder root,
        string canonicalPath,
        DirectoryObjectIdentityResolution currentGeneration,
        string? detail) =>
        new(
            RootFolderStorageState.Limited,
            RootFolderStorageReason.IdentityUnsupported,
            "This folder uses a legacy Linux identity that is no longer strong enough for safe moves and deletions. Listenarr can still read and scan it; review and confirm the current folder to upgrade its identity.",
            CanConfirmCurrentFolder: true,
            CanChangePath: true,
            CanMutateFilesystem: false,
            ConfirmationToken: CreateConfirmationToken(
                root,
                canonicalPath,
                currentGeneration),
            Detail: detail);

    private static RootFolderStorageObservation FromFailure(
        DirectoryObjectIdentityResolution resolution)
    {
        return resolution.FailureKind switch
        {
            DirectoryObjectIdentityFailureKind.Missing => new RootFolderStorageObservation(
                RootFolderStorageState.Missing,
                RootFolderStorageReason.PathMissing,
                "This folder is not currently available.",
                CanConfirmCurrentFolder: false,
                CanChangePath: true,
                CanMutateFilesystem: false,
                ConfirmationToken: null),
            DirectoryObjectIdentityFailureKind.ForeignPathSyntax =>
                Unavailable(RootFolderStorageReason.ForeignPathSyntax, resolution.UnavailableReason),
            DirectoryObjectIdentityFailureKind.AccessDenied =>
                Unavailable(RootFolderStorageReason.AccessDenied, resolution.UnavailableReason),
            DirectoryObjectIdentityFailureKind.IdentityUnsupported =>
                Unavailable(RootFolderStorageReason.IdentityUnsupported, resolution.UnavailableReason),
            DirectoryObjectIdentityFailureKind.LegacyWeakIdentity =>
                Unavailable(RootFolderStorageReason.IdentityUnsupported, resolution.UnavailableReason),
            DirectoryObjectIdentityFailureKind.IdentityUnstable =>
                Unavailable(RootFolderStorageReason.IdentityUnstable, resolution.UnavailableReason),
            DirectoryObjectIdentityFailureKind.InvalidPath =>
                Unavailable(RootFolderStorageReason.InvalidPath, resolution.UnavailableReason),
            _ => Unavailable(RootFolderStorageReason.Unknown, resolution.UnavailableReason)
        };
    }

    private static RootFolderStorageObservation Unavailable(
        RootFolderStorageReason reason,
        string? detail) =>
        new(
            RootFolderStorageState.Unavailable,
            reason,
            reason switch
            {
                RootFolderStorageReason.ForeignPathSyntax =>
                    "This configured path belongs to a different operating system and cannot be used on this host.",
                RootFolderStorageReason.AccessDenied =>
                    "Listenarr cannot access this folder. Check the storage permissions and mount settings.",
                RootFolderStorageReason.IdentityUnsupported =>
                    "This storage location does not expose the directory identity Listenarr requires for safe filesystem operations.",
                RootFolderStorageReason.IdentityUnstable =>
                    "This folder changed while Listenarr was checking it. Refresh the storage state and try again.",
                RootFolderStorageReason.FilesystemSemanticsUnavailable =>
                    "Listenarr cannot determine this storage location's path rules safely. Review the root folder case-sensitivity setting.",
                RootFolderStorageReason.FilesystemSemanticsChanged =>
                    "This storage location now uses different case-sensitivity rules. Review the root folder settings before using it for filesystem operations.",
                RootFolderStorageReason.InvalidPath =>
                    "The configured storage path is invalid on this host.",
                _ => "Listenarr cannot verify this storage location."
            },
            CanConfirmCurrentFolder: false,
            CanChangePath: true,
            CanMutateFilesystem: false,
            ConfirmationToken: null,
            Detail: detail);
}
