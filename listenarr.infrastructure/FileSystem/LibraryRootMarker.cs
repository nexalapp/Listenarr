using System.ComponentModel;
using System.Text;

namespace Listenarr.Infrastructure.FileSystem;

internal sealed class LibraryRootMarkerStore : ILibraryRootMarkerStore
{
    internal const string MarkerFileName = "listenarr-library.id";
    private const string MarkerPrefix = "listenarr-library-v1:";
    private const int MaximumMarkerBytes = 8192;

    public LibraryRootMarkerReading Read(string canonicalPath, Guid expectedMarkerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        try
        {
            using var anchor = PinnedDirectoryCreation.OpenPinnedBoundary(canonicalPath);
            if (!anchor.VisiblePathMatches())
            {
                return new LibraryRootMarkerReading(
                    LibraryRootMarkerState.Unreadable,
                    "The root directory changed while its library marker was being read.");
            }

            return ReadFromAnchor(anchor, expectedMarkerId);
        }
        catch (Exception exception) when (IsExpectedFilesystemFailure(exception))
        {
            return new LibraryRootMarkerReading(
                LibraryRootMarkerState.Unreadable,
                exception.Message);
        }
    }

    public Guid Enroll(string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        using var anchor = PinnedDirectoryCreation.OpenPinnedBoundary(canonicalPath);
        if (TryReadMarkerId(anchor, out var existing))
        {
            return existing;
        }

        var markerId = Guid.NewGuid();
        using (var created = anchor.CreateNewFile(MarkerFileName))
        {
            using var stream = created.OpenIndependentReadWriteStream(
                bufferSize: 4096,
                asynchronous: false);
            var bytes = Encoding.UTF8.GetBytes(CreateMarkerContent(markerId));
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            stream.Flush(flushToDisk: true);
        }

        // The marker is only useful if it outlives the crash that loses the mount, so
        // the directory entry itself has to reach the media before enrollment is
        // reported as durable.
        anchor.FlushDirectoryEntry();
        return markerId;
    }

    private static LibraryRootMarkerReading ReadFromAnchor(
        PinnedDirectoryCreation.PinnedDirectoryAnchor anchor,
        Guid expectedMarkerId)
    {
        var outcome = anchor.TryOpenExistingFileWithOutcome(
            MarkerFileName,
            requireDeleteAccess: false,
            out var entry);
        using (entry)
        {
            switch (outcome)
            {
                case PinnedFileOpenOutcome.NotFound:
                    return new LibraryRootMarkerReading(
                        LibraryRootMarkerState.Missing,
                        $"No {MarkerFileName} is present in the root directory.");
                case PinnedFileOpenOutcome.Opened when entry != null:
                    return ClassifyMarker(entry, expectedMarkerId);
                default:
                    return new LibraryRootMarkerReading(
                        LibraryRootMarkerState.Unreadable,
                        $"{MarkerFileName} could not be opened.");
            }
        }
    }

    private static LibraryRootMarkerReading ClassifyMarker(
        PinnedDirectoryCreation.PinnedFileEntry entry,
        Guid expectedMarkerId)
    {
        if (!entry.IsRegularFile())
        {
            return new LibraryRootMarkerReading(
                LibraryRootMarkerState.Unreadable,
                $"{MarkerFileName} is not a regular file.");
        }

        if (!TryParseMarkerId(ReadMarkerContent(entry), out var observed))
        {
            return new LibraryRootMarkerReading(
                LibraryRootMarkerState.Unreadable,
                $"{MarkerFileName} does not carry a readable library identifier.");
        }

        return observed == expectedMarkerId
            ? new LibraryRootMarkerReading(LibraryRootMarkerState.Matched)
            : new LibraryRootMarkerReading(
                LibraryRootMarkerState.Mismatched,
                "The directory carries a different library's marker.");
    }

    private static bool TryReadMarkerId(
        PinnedDirectoryCreation.PinnedDirectoryAnchor anchor,
        out Guid markerId)
    {
        markerId = Guid.Empty;
        var outcome = anchor.TryOpenExistingFileWithOutcome(
            MarkerFileName,
            requireDeleteAccess: false,
            out var entry);
        using (entry)
        {
            return outcome == PinnedFileOpenOutcome.Opened
                && entry != null
                && entry.IsRegularFile()
                && TryParseMarkerId(ReadMarkerContent(entry), out markerId);
        }
    }

    private static string ReadMarkerContent(
        PinnedDirectoryCreation.PinnedFileEntry entry)
    {
        using var stream = entry.OpenIndependentReadStream(
            bufferSize: 4096,
            asynchronous: false);
        var buffer = new byte[MaximumMarkerBytes];
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = stream.Read(buffer, read, buffer.Length - read);
            if (chunk == 0)
            {
                break;
            }

            read += chunk;
        }

        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static bool TryParseMarkerId(string content, out Guid markerId)
    {
        markerId = Guid.Empty;
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(MarkerPrefix, StringComparison.Ordinal)
                && Guid.TryParseExact(
                    trimmed[MarkerPrefix.Length..].Trim(),
                    "D",
                    out markerId))
            {
                return true;
            }
        }

        return false;
    }

    private static string CreateMarkerContent(Guid markerId) =>
        FormattableString.Invariant($"""
            {MarkerPrefix}{markerId:D}

            This file identifies the directory it sits in as a Listenarr library root.
            Listenarr reads it to tell a real library from an empty mount point, so that
            it refuses to move, rename or delete anything when the storage is not mounted.

            Keep it here, and do not copy it into a different library root.

            """);

    private static bool IsExpectedFilesystemFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or Win32Exception
            or PlatformNotSupportedException
            or InvalidOperationException;
}
