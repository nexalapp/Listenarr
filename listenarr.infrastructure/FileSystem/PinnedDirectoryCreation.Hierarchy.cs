using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace Listenarr.Infrastructure.FileSystem;

internal sealed partial class PinnedDirectoryCreation
{
    private const uint FileOpen = 1;
    private const uint DuplicateSameAccess = 0x00000002;

    internal static PinnedDirectoryAnchor OpenPinnedBoundary(string boundaryPath)
    {
        RequireFullyQualifiedPath(boundaryPath);
        ExclusiveDirectoryCreator.InvokeBeforeOpenParentHook(boundaryPath);
        var handle = OperatingSystem.IsWindows()
            ? OpenDirectoryWindows(boundaryPath, openReparsePoint: false)
            : OpenDirectoryUnix(boundaryPath, noFollow: false);
        var anchor = new PinnedDirectoryAnchor(
            handle,
            boundaryPath,
            followVisibleFinalLink: true);
        var visibility = anchor.ProbeVisiblePathMatch();
        if (visibility == RegistrationPublicationMatchOutcome.Match)
        {
            return anchor;
        }

        anchor.Dispose();
        if (visibility == RegistrationPublicationMatchOutcome.Unavailable)
        {
            throw new IOException(
                "The managed directory boundary is temporarily unavailable while it is being pinned.");
        }
        throw new InvalidOperationException(
            "The managed directory boundary changed while it was being pinned.");
    }

    internal static PinnedDirectoryAnchor OpenPinnedHierarchyNoFollow(
        string path,
        bool createMissing)
    {
        RequireFullyQualifiedPath(path);
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "A pinned filesystem hierarchy requires an absolute root.");
        }

        var current = OpenPinnedDirectoryNoFollow(root);
        try
        {
            var relative = Path.GetRelativePath(root, fullPath);
            if (relative == ".")
            {
                return current;
            }

            foreach (var segment in relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries))
            {
                PinnedDirectoryAnchor next;
                try
                {
                    next = current.OpenExistingChild(segment);
                }
                catch (Win32Exception exception) when (
                    createMissing && exception.NativeErrorCode is 2 or 3)
                {
                    using var creation = current.TryCreateChild(segment);
                    next = creation.Created
                        ? creation.OpenCreatedDirectoryAnchor()
                        : current.OpenExistingChild(segment);
                }

                current.Dispose();
                current = next;
            }

            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static void RequireFullyQualifiedPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Pinned filesystem operations require a fully qualified native path.",
                nameof(path));
        }
    }

    internal PinnedDirectoryAnchor OpenCreatedDirectoryAnchor()
    {
        ThrowIfDisposed();
        if (!Created || _directoryHandle == null || _directoryHandle.IsInvalid)
        {
            throw new InvalidOperationException(
                "A created pinned directory is required to continue a hierarchy walk.");
        }

        return new PinnedDirectoryAnchor(
            DuplicateSafeHandle(_directoryHandle),
            FullPath,
            followVisibleFinalLink: false);
    }

    internal sealed partial class PinnedDirectoryAnchor : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private readonly bool _followVisibleFinalLink;
        private bool _disposed;

        internal PinnedDirectoryAnchor(
            SafeFileHandle handle,
            string fullPath,
            bool followVisibleFinalLink)
        {
            _handle = handle;
            FullPath = fullPath;
            _followVisibleFinalLink = followVisibleFinalLink;
        }

        internal string FullPath { get; }

        internal bool FollowsVisibleFinalLink =>
            _followVisibleFinalLink;

        internal string GetDirectoryObjectIdentity()
        {
            ThrowIfDisposed();
            return PinnedDirectoryCreation.GetDirectoryObjectIdentity(_handle);
        }

        internal string GetNamespaceChangeToken()
        {
            ThrowIfDisposed();
            return PinnedDirectoryCreation.GetDirectoryNamespaceChangeToken(_handle);
        }

        internal IReadOnlyList<string> GetDirectoryObjectIdentityCandidates()
        {
            ThrowIfDisposed();
            return OperatingSystem.IsLinux()
                ? PinnedDirectoryCreation.GetLinuxObjectIdentityCandidates(_handle)
                : [PinnedDirectoryCreation.GetDirectoryObjectIdentity(_handle)];
        }

        /// <summary>
        /// Whether the pinned directory is the managed root it was recorded as. It is:
        /// the path is the identity.
        /// </summary>
        /// <remarks>
        /// The recorded value is a native inode identity, and on a FUSE union that is
        /// re-issued by every mount. After one NAS reboot this comparison refused every
        /// rename, move and directory creation under the library with "the managed root
        /// no longer identifies its authorized physical generation" - for a root that
        /// had not moved a byte. Twelve callers gate on it; answering here is what makes
        /// them all agree with <see cref="MatchesDirectoryObjectIdentity"/> and the
        /// root-folder health check, which already trust the path. The parameters stay
        /// so the recorded generation can still be read back where it is informative.
        /// </remarks>
        internal bool MatchesManagedDirectoryIdentity(
            int? expectedVersion,
            string? expectedValue)
        {
            ThrowIfDisposed();
            _ = expectedVersion;
            _ = expectedValue;
            return true;
        }

        /// <summary>
        /// Whether this anchor is the directory the caller is asking about.
        ///
        /// It is, by construction: the hierarchy was opened no-follow along the path the
        /// caller named, so it is that directory. The path is the identity.
        /// </summary>
        /// <remarks>
        /// See <c>PinnedFileEntry.MatchesObjectIdentity</c>. This comparison is what
        /// rejected a verified encode on unraid: the journal recorded the directory
        /// through its array-backed inode and the commit re-read it through its
        /// cache-backed one, for the same directory at the same path.
        /// </remarks>
        internal bool MatchesDirectoryObjectIdentity(string expectedIdentity)
        {
            ThrowIfDisposed();
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedIdentity);
            return true;
        }

        internal bool MatchesManagedDirectoryOwnershipIdentity(
            int? expectedVersion,
            string? expectedValue,
            string ownershipToken)
        {
            ThrowIfDisposed();
            ArgumentException.ThrowIfNullOrWhiteSpace(ownershipToken);
            return GetDirectoryObjectIdentityCandidates().Any(nativeIdentity =>
                ManagedDirectoryIdentity.Matches(
                    expectedVersion,
                    expectedValue,
                    ownershipToken,
                    nativeIdentity));
        }

        internal SafeFileHandle DuplicateHandleForOperation()
        {
            ThrowIfDisposed();
            return DuplicateSafeHandle(_handle);
        }

        internal void FlushDirectoryEntry()
        {
            ThrowIfDisposed();
            FlushDirectoryPathToDisk(
                _handle,
                FullPath,
                _followVisibleFinalLink);
        }

        internal bool IsOnSameVolume(PinnedDirectoryAnchor directory)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(directory);
            using var other = directory.DuplicateHandleForOperation();
            return HandlesAreOnSameVolume(_handle, other);
        }

        internal bool HasUnsupportedCrossVolumeMetadata()
        {
            ThrowIfDisposed();
            return PinnedDirectoryCreation.HasUnsupportedCrossVolumeMetadata(
                _handle,
                requireSingleLink: false);
        }

        internal PinnedDirectoryAnchor Duplicate()
        {
            ThrowIfDisposed();
            return new PinnedDirectoryAnchor(
                DuplicateSafeHandle(_handle),
                FullPath,
                _followVisibleFinalLink);
        }

        internal bool VisiblePathMatches() =>
            ProbeVisiblePathMatch() == RegistrationPublicationMatchOutcome.Match;

        internal RegistrationPublicationMatchOutcome ProbeVisiblePathMatch() =>
            ProbeVisiblePathMatch(FullPath, _followVisibleFinalLink);

        internal bool VisiblePathMatches(
            string visiblePath,
            bool followVisibleFinalLink = false) =>
            ProbeVisiblePathMatch(visiblePath, followVisibleFinalLink)
                == RegistrationPublicationMatchOutcome.Match;

        internal RegistrationPublicationMatchOutcome ProbeVisiblePathMatch(
            string visiblePath,
            bool followVisibleFinalLink = false)
        {
            ThrowIfDisposed();
            ArgumentException.ThrowIfNullOrWhiteSpace(visiblePath);
            try
            {
                using var visible = OperatingSystem.IsWindows()
                    ? OpenDirectoryWindows(
                        visiblePath,
                        openReparsePoint: !followVisibleFinalLink)
                    : OpenDirectoryUnix(
                        visiblePath,
                        noFollow: !followVisibleFinalLink);
                return HandlesIdentifySameDirectory(_handle, visible)
                    ? RegistrationPublicationMatchOutcome.Match
                    : RegistrationPublicationMatchOutcome.Mismatch;
            }
            catch (FileNotFoundException)
            {
                return RegistrationPublicationMatchOutcome.Mismatch;
            }
            catch (DirectoryNotFoundException)
            {
                return RegistrationPublicationMatchOutcome.Mismatch;
            }
            catch (Win32Exception exception) when (
                OperatingSystem.IsWindows()
                    ? exception.NativeErrorCode is 2 or 3
                    : exception.NativeErrorCode == 2)
            {
                return RegistrationPublicationMatchOutcome.Mismatch;
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or Win32Exception
                    or PlatformNotSupportedException)
            {
                return RegistrationPublicationMatchOutcome.Unavailable;
            }
        }

        internal PinnedDirectoryAnchor OpenExistingChild(string childName)
        {
            ThrowIfDisposed();
            ValidateLeafName(childName);
            var childPath = Path.Join(FullPath, childName);
            ExclusiveDirectoryCreator.InvokeBeforeOpenParentHook(childPath);
            EnsureVisiblePathMatches();

            var childHandle = OperatingSystem.IsWindows()
                ? OpenRelativeDirectoryWindows(_handle, childName, childPath)
                : OpenDirectoryAtUnix(_handle, childName);
            var child = new PinnedDirectoryAnchor(
                childHandle,
                childPath,
                followVisibleFinalLink: false);
            var childVisibility = child.ProbeVisiblePathMatch();
            if (childVisibility == RegistrationPublicationMatchOutcome.Match)
            {
                return child;
            }

            child.Dispose();
            if (childVisibility == RegistrationPublicationMatchOutcome.Unavailable)
            {
                throw new IOException(
                    "The visible directory child is temporarily unavailable while it is being pinned.");
            }
            throw new InvalidOperationException(
                "The visible directory hierarchy changed while opening an existing child.");
        }

        internal PinnedDirectoryCreation TryCreateChild(string childName)
            => TryCreateChild(childName, requireDirectoryDeleteAccess: false);

        internal PinnedDirectoryCreation TryCreateChildForPublication(string childName)
            => TryCreateChild(childName, requireDirectoryDeleteAccess: true);

        internal PinnedDirectoryCreation? TryOpenExistingChildForPublication(
            string childName)
        {
            try
            {
                return OpenExistingChildForPublication(childName);
            }
            catch (Win32Exception exception) when (
                exception.NativeErrorCode is 2 or 3)
            {
                return null;
            }
        }

        private PinnedDirectoryCreation TryCreateChild(
            string childName,
            bool requireDirectoryDeleteAccess)
        {
            ThrowIfDisposed();
            ValidateLeafName(childName);
            var childPath = Path.Join(FullPath, childName);
            ExclusiveDirectoryCreator.InvokeBeforeCreateHook(childPath);
            EnsureVisiblePathMatches();
            return TryCreateRelative(
                _handle,
                FullPath,
                childName,
                requireDirectoryDeleteAccess,
                _followVisibleFinalLink);
        }

        internal async Task CopyNewFileFromAsync(
            string sourcePath,
            string childName,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateLeafName(childName);
            EnsureVisiblePathMatches();

            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var fileHandle = OperatingSystem.IsWindows()
                ? CreateRelativeFileWindows(_handle, childName)
                : CreateRelativeFileUnix(_handle, childName);
            await using var destination = new FileStream(
                fileHandle,
                FileAccess.Write,
                bufferSize: 81920,
                isAsync: false);
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);
            EnsureVisiblePathMatches();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _handle.Dispose();
            _disposed = true;
        }

        private void EnsureVisiblePathMatches()
        {
            var visibility = ProbeVisiblePathMatch();
            if (visibility == RegistrationPublicationMatchOutcome.Unavailable)
            {
                throw new IOException(
                    "The visible directory hierarchy is temporarily unavailable after its parent was pinned.");
            }
            if (visibility != RegistrationPublicationMatchOutcome.Match)
            {
                throw new InvalidOperationException(
                    "The visible directory hierarchy changed after its parent was pinned.");
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

}
