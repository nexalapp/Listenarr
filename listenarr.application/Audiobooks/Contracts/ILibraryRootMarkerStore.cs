namespace Listenarr.Application.Audiobooks.Contracts;

public enum LibraryRootMarkerState
{
    /// <summary>
    /// The marker could not be read, so nothing can be concluded from it. First so that
    /// a default-valued reading can never be mistaken for proof of anything.
    /// </summary>
    Unreadable,

    /// <summary>The marker is present and names the enrolled library.</summary>
    Matched,

    /// <summary>A marker is present, but it names a different library.</summary>
    Mismatched,

    /// <summary>
    /// No marker is present. For an enrolled root this is the signal that the storage
    /// is not mounted: the marker lives on the library media, so an empty mount point
    /// auto-created by a container runtime can never produce one.
    /// </summary>
    Missing
}

public readonly record struct LibraryRootMarkerReading(
    LibraryRootMarkerState State,
    string? Detail = null);

/// <summary>
/// A root folder's durable identity: a small file written into the library itself.
///
/// The native directory identity (device number, inode, file handle) is kernel state,
/// re-issued whenever the filesystem is remounted, so it cannot survive a reboot, an
/// array restart, or a FUSE share being remounted - all of which leave the library
/// completely intact. This marker is data on the media instead, so it survives all of
/// them, while still failing closed for the case the identity check exists to catch:
/// a missing mount whose empty mount point still resolves at the configured path.
/// </summary>
public interface ILibraryRootMarkerStore
{
    LibraryRootMarkerReading Read(string canonicalPath, Guid expectedMarkerId);

    /// <summary>
    /// Adopts the marker already in the directory, or writes a new one. Adoption keeps
    /// re-confirmation idempotent and never needs delete authority over library media.
    /// </summary>
    Guid Enroll(string canonicalPath);
}
