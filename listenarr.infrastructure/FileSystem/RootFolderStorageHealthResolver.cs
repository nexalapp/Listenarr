using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.FileSystem;

internal sealed partial class RootFolderStorageHealthResolver(
    IDirectoryObjectIdentityResolver identityResolver,
    IFileSystemSemanticsResolver? semanticsResolver = null,
    Func<string, bool?>? readOnlyFileSystemProbe = null,
    ILibraryRootMarkerStore? markerStore = null)
    : IRootFolderStorageHealthResolver
{
    private const string ConfirmationTokenVersion = "root-storage-v1";
    private readonly IFileSystemSemanticsResolver _semanticsResolver =
        semanticsResolver ?? new FileSystemSemanticsResolver();
    private readonly Func<string, bool?> _readOnlyFileSystemProbe =
        readOnlyFileSystemProbe ?? ProbeReadOnlyFileSystem;
    private readonly ILibraryRootMarkerStore _markerStore =
        markerStore ?? new LibraryRootMarkerStore();

    public async Task<RootFolderStorageObservation> ResolveAsync(
        RootFolder root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();

        if (!FileSystemPathIdentity.TryCanonicalizeUnambiguousStoredAbsolutePathForHost(
                root.Path,
                out var canonicalPath,
                out var pathReason))
        {
            var reason = FileSystemPathIdentity.TryDetectAbsoluteSyntax(root.Path, out _)
                && !FileSystemPathIdentity.TryDetectAbsoluteSyntaxForHost(root.Path, out _)
                    ? RootFolderStorageReason.ForeignPathSyntax
                    : RootFolderStorageReason.InvalidPath;
            return Unavailable(reason, pathReason);
        }

        // A root that carries a marker answers "is this my library?" from the media
        // itself, so remounts, reboots and array restarts - all of which re-issue the
        // native directory identity while leaving every byte in place - stop being
        // events the operator has to acknowledge.
        //
        // The marker only ever rescues a case the native identity would have failed.
        // Every other outcome falls through untouched, so no operator loses a recovery
        // path they had before a marker existed.
        LibraryRootMarkerReading? marker = root.LibraryMarkerId.HasValue
            ? _markerStore.Read(canonicalPath, root.LibraryMarkerId.Value)
            : null;
        if (marker?.State == LibraryRootMarkerState.Matched)
        {
            return await ValidateFilesystemSemanticsAsync(
                root,
                canonicalPath,
                new RootFolderStorageObservation(
                    RootFolderStorageState.Healthy,
                    RootFolderStorageReason.None,
                    null,
                    CanConfirmCurrentFolder: false,
                    CanChangePath: true,
                    CanMutateFilesystem: true,
                    ConfirmationToken: null),
                cancellationToken);
        }

        var observation = await ResolveFromNativeIdentityAsync(
            root,
            canonicalPath,
            cancellationToken);

        // An enrolled root whose marker is gone is very likely storage that never
        // mounted, and writing into an empty mount point is the one thing worth
        // refusing. Unlike a re-issued native identity, a missing marker is evidence:
        // the file this app put on the media is not there. Confirming re-enrols it.
        if (marker?.State != LibraryRootMarkerState.Missing)
        {
            return observation;
        }

        return observation with
        {
            State = RootFolderStorageState.Changed,
            Reason = RootFolderStorageReason.IdentityMismatch,
            CanMutateFilesystem = false,
            CanConfirmCurrentFolder = true,
            Detail = string.IsNullOrWhiteSpace(observation.Detail)
                ? LibraryMarkerAbsentDetail
                : $"{observation.Detail} {LibraryMarkerAbsentDetail}"
        };
    }

    private const string LibraryMarkerAbsentDetail =
        "This root's library marker file is also absent, which usually means the storage is not mounted rather than that the folder was replaced.";

    private async Task<RootFolderStorageObservation> ResolveFromNativeIdentityAsync(
        RootFolder root,
        string canonicalPath,
        CancellationToken cancellationToken)
    {
        var hasAuthorizedIdentity = root.DirectoryObjectIdentityVersion.HasValue
            && !string.IsNullOrWhiteSpace(root.DirectoryObjectIdentity);
        if (!hasAuthorizedIdentity)
        {
            var current = await identityResolver.ResolveAsync(canonicalPath, cancellationToken);
            if (!current.IsAvailable)
            {
                if (current.FailureKind == DirectoryObjectIdentityFailureKind.IdentityUnsupported)
                {
                    return await ValidateFilesystemSemanticsAsync(
                        root,
                        canonicalPath,
                        LimitedIdentityUnsupported(current.UnavailableReason),
                        cancellationToken);
                }

                return FromFailure(current);
            }

            // The path is the identity. A directory that is where the root says it is
            // is the library; confirming it merely enrols the marker that makes later
            // remounts quieter, and is offered rather than required.
            return await ValidateFilesystemSemanticsAsync(
                root,
                canonicalPath,
                new RootFolderStorageObservation(
                    RootFolderStorageState.Healthy,
                    RootFolderStorageReason.None,
                    null,
                    CanConfirmCurrentFolder: true,
                    CanChangePath: true,
                    CanMutateFilesystem: true,
                    ConfirmationToken: CreateConfirmationToken(root, canonicalPath, current)),
                cancellationToken);
        }

        var expected = await identityResolver.ResolveExistingAsync(
            canonicalPath,
            root.DirectoryObjectIdentityVersion!.Value,
            root.DirectoryObjectIdentity!,
            cancellationToken);
        if (expected.IsAvailable)
        {
            return await ValidateFilesystemSemanticsAsync(
                root,
                canonicalPath,
                new RootFolderStorageObservation(
                    RootFolderStorageState.Healthy,
                    RootFolderStorageReason.None,
                    null,
                    CanConfirmCurrentFolder: false,
                    CanChangePath: true,
                    CanMutateFilesystem: true,
                    ConfirmationToken: null),
                cancellationToken);
        }

        if (expected.FailureKind == DirectoryObjectIdentityFailureKind.IdentityUnsupported)
        {
            var unsupportedCurrentGeneration = await identityResolver.ResolveAsync(
                canonicalPath,
                cancellationToken);
            if (unsupportedCurrentGeneration.IsAvailable)
            {
                return await ValidateFilesystemSemanticsAsync(
                    root,
                    canonicalPath,
                    UnsupportedPersistedIdentity(
                        root,
                        canonicalPath,
                        unsupportedCurrentGeneration,
                        expected.UnavailableReason),
                    cancellationToken);
            }
            if (unsupportedCurrentGeneration.FailureKind
                != DirectoryObjectIdentityFailureKind.IdentityUnsupported)
            {
                return FromFailure(unsupportedCurrentGeneration);
            }

            return await ValidateFilesystemSemanticsAsync(
                root,
                canonicalPath,
                LimitedIdentityUnsupported(
                    unsupportedCurrentGeneration.UnavailableReason
                        ?? expected.UnavailableReason),
                cancellationToken);
        }

        if (expected.FailureKind == DirectoryObjectIdentityFailureKind.LegacyWeakIdentity)
        {
            var legacyCurrentGeneration = await identityResolver.ResolveAsync(
                canonicalPath,
                cancellationToken);
            if (!legacyCurrentGeneration.IsAvailable)
            {
                return FromFailure(legacyCurrentGeneration);
            }

            return await ValidateFilesystemSemanticsAsync(
                root,
                canonicalPath,
                LimitedLegacyIdentity(
                    root,
                    canonicalPath,
                    legacyCurrentGeneration,
                    expected.UnavailableReason),
                cancellationToken);
        }

        if (expected.FailureKind != DirectoryObjectIdentityFailureKind.IdentityMismatch)
        {
            return FromFailure(expected);
        }

        // Resolve the currently visible generation separately so the confirmation token
        // is bound to the exact replacement generation the user is being asked to confirm.
        var currentGeneration = await identityResolver.ResolveAsync(canonicalPath, cancellationToken);
        if (!currentGeneration.IsAvailable)
        {
            return FromFailure(currentGeneration);
        }

        // A native identity that no longer matches is what every reboot of a FUSE
        // union filesystem produces, not evidence of a swapped folder. The path is
        // the identity; nothing is blocked. Confirming re-enrols the marker and
        // records the new generation, and stays available for anyone who wants it.
        return await ValidateFilesystemSemanticsAsync(
            root,
            canonicalPath,
            new RootFolderStorageObservation(
                RootFolderStorageState.Healthy,
                RootFolderStorageReason.None,
                null,
                CanConfirmCurrentFolder: true,
                CanChangePath: true,
                CanMutateFilesystem: true,
                ConfirmationToken: CreateConfirmationToken(root, canonicalPath, currentGeneration)),
            cancellationToken);
    }

    private async Task<RootFolderStorageObservation> ValidateFilesystemSemanticsAsync(
        RootFolder root,
        string canonicalPath,
        RootFolderStorageObservation observation,
        CancellationToken cancellationToken)
    {
        var currentSemantics = await _semanticsResolver.ResolveAsync(
            canonicalPath,
            root.CaseSensitivityMode,
            cancellationToken);
        if (currentSemantics.State != PathIdentityState.Valid)
        {
            return SemanticsUnavailable(
                observation,
                RootFolderStorageReason.FilesystemSemanticsUnavailable,
                currentSemantics.Reason);
        }

        var persistedSemantics = RootFolderPathSemantics.ResolvePersisted(root);
        if (persistedSemantics == null
            || persistedSemantics.Value.DetectAmbiguousCaseMatches)
        {
            // A legacy root has no prior filesystem semantics to compare against.
            // That is not a reason to block it; confirming the folder records them.
            return observation;
        }

        if (persistedSemantics.Value.Semantics.CaseSensitivity
            != currentSemantics.Semantics.CaseSensitivity)
        {
            return SemanticsUnavailable(
                observation,
                RootFolderStorageReason.FilesystemSemanticsChanged,
                $"Persisted case sensitivity is {persistedSemantics.Value.Semantics.CaseSensitivity}, but the live storage resolves as {currentSemantics.Semantics.CaseSensitivity}.");
        }

        if (observation.State != RootFolderStorageState.Healthy)
        {
            var limitedReadOnly = _readOnlyFileSystemProbe(canonicalPath);
            if (limitedReadOnly != false)
            {
                return ApplyMutationCapability(
                    canonicalPath,
                    observation,
                    limitedReadOnly);
            }

            return observation with
            {
                CanPublishNewFiles =
                    observation.State == RootFolderStorageState.Limited
                    && observation.Reason == RootFolderStorageReason.IdentityUnsupported
                    && !observation.CanConfirmCurrentFolder
            };
        }

        var mutationCapability = ApplyMutationCapability(
            canonicalPath,
            observation,
            _readOnlyFileSystemProbe(canonicalPath));
        if (!mutationCapability.CanMutateFilesystem)
        {
            return mutationCapability;
        }

        if (root.CaseSensitivityMode == FileSystemCaseSensitivityMode.Auto
            && !currentSemantics.HasDurableMutationSemanticsAuthority)
        {
            return mutationCapability with
            {
                State = RootFolderStorageState.Limited,
                Reason = RootFolderStorageReason.MutationSemanticsUnproven,
                Message =
                    "Listenarr can read and scan this storage, but automatic case-sensitivity detection is not stable enough to authorize filesystem mutations. Select Sensitive or Insensitive explicitly to enable moves, deletes, and other writes.",
                CanConfirmCurrentFolder = false,
                CanMutateFilesystem = false,
                CanPublishNewFiles = false,
                ConfirmationToken = null,
                Detail =
                    "Automatic case sensitivity was inferred from an existing directory entry rather than an authoritative filesystem capability."
            };
        }

        return mutationCapability;
    }
}
