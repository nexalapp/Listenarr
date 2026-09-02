using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Listenarr.Infrastructure.FileSystem;

internal sealed partial class PinnedDirectoryCreation
{
    internal sealed partial class PinnedDirectoryAnchor
    {
    }

    private static int TryRenameRelativeEntryNoReplaceLinux(
        SafeFileHandle sourceDirectoryHandle,
        string sourceName,
        SafeFileHandle destinationDirectoryHandle,
        string finalName)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "The non-throwing no-replace rename probe is Linux-specific.");
        }

        var result = RenameAtNoReplaceLinux(
            sourceDirectoryHandle.DangerousGetHandle().ToInt32(),
            sourceName,
            destinationDirectoryHandle.DangerousGetHandle().ToInt32(),
            finalName,
            RenameNoReplace);
        return result == 0 ? 0 : Marshal.GetLastWin32Error();
    }

    private static void RenameRelativeEntry(
        SafeFileHandle sourceDirectoryHandle,
        SafeFileHandle entryHandle,
        string sourceName,
        SafeFileHandle destinationDirectoryHandle,
        string finalName,
        bool entryIsDirectory,
        bool replaceExisting = false)
    {
        if (OperatingSystem.IsWindows())
        {
            RenameRelativeEntryWindows(
                destinationDirectoryHandle,
                entryHandle,
                finalName,
                replaceExisting);
            return;
        }

        var sourceDirectoryFileDescriptor = sourceDirectoryHandle
            .DangerousGetHandle()
            .ToInt32();
        var destinationDirectoryFileDescriptor = destinationDirectoryHandle
            .DangerousGetHandle()
            .ToInt32();
        var result = replaceExisting
            ? RenameAtUnix(
                sourceDirectoryFileDescriptor,
                sourceName,
                destinationDirectoryFileDescriptor,
                finalName)
            : OperatingSystem.IsMacOS()
            ? RenameAtExclusiveMac(
                sourceDirectoryFileDescriptor,
                sourceName,
                destinationDirectoryFileDescriptor,
                finalName,
                RenameExclusiveMac)
            : RenameAtNoReplaceLinux(
                sourceDirectoryFileDescriptor,
                sourceName,
                destinationDirectoryFileDescriptor,
                finalName,
                RenameNoReplace);
        if (result == 0)
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();

        // renameat2's RENAME_NOREPLACE is a kernel flag the filesystem driver has to
        // implement, and FUSE filesystems generally do not: shfs, which is what an
        // unraid user share is, rejects it with EINVAL. Without a fallback every rename
        // into such a library fails, which is every organise and every import on the
        // most common deployment there is.
        if (!replaceExisting
            && !OperatingSystem.IsMacOS()
            && IsUnsupportedRenameFlagError(error))
        {
            var fallbackError = entryIsDirectory
                ? TryPublishDirectoryByReservingLinux(
                    sourceDirectoryFileDescriptor,
                    sourceName,
                    destinationDirectoryFileDescriptor,
                    finalName)
                : TryPublishByLinkingLinux(
                    sourceDirectoryHandle,
                    sourceName,
                    destinationDirectoryHandle,
                    finalName);
            if (fallbackError == 0)
            {
                return;
            }

            throw new Win32Exception(
                fallbackError,
                "Could not publish a pinned filesystem entry relative to its owned directory.");
        }

        throw new Win32Exception(
            error,
            "Could not publish a pinned filesystem entry relative to its owned directory.");
    }

    /// <summary>
    /// Publish without RENAME_NOREPLACE, keeping the guarantee it was there for, and
    /// reporting errno rather than throwing so it fits the probe-style Linux path.
    ///
    /// linkat refuses an existing destination with EEXIST, so the no-clobber promise is
    /// the filesystem's rather than a check this code races. Two steps instead of one,
    /// but the window between them holds a file reachable under both names - the same
    /// inode, the same bytes - rather than one that is missing or half written.
    /// </summary>
    internal static int TryPublishByLinkingLinux(
        SafeFileHandle sourceDirectoryHandle,
        string sourceName,
        SafeFileHandle destinationDirectoryHandle,
        string finalName)
    {
        var sourceDirectoryFileDescriptor = sourceDirectoryHandle.DangerousGetHandle().ToInt32();
        var destinationDirectoryFileDescriptor = destinationDirectoryHandle
            .DangerousGetHandle()
            .ToInt32();

        if (LinkAt(
                sourceDirectoryFileDescriptor,
                sourceName,
                destinationDirectoryFileDescriptor,
                finalName,
                0) != 0)
        {
            return Marshal.GetLastWin32Error();
        }

        if (UnlinkAt(sourceDirectoryFileDescriptor, sourceName, 0) != 0)
        {
            // The destination is published and correct; only the name it came from is
            // left over. Both names refer to the same file, so nothing needs re-copying.
            return Marshal.GetLastWin32Error();
        }

        return 0;
    }

    /// <summary>
    /// The directory equivalent, which cannot be a link: hard links to directories are
    /// refused on Linux, so the file fallback fails with EPERM here.
    ///
    /// Reserving the name with mkdirat carries the refusal instead - it fails with
    /// EEXIST when the name is taken - and renaming onto a directory this call just
    /// created is allowed because it is empty. What the flag guaranteed atomically is
    /// therefore split in two, and the only thing that fits in the gap is the empty
    /// directory this code owns.
    /// </summary>
    private static int TryPublishDirectoryByReservingLinux(
        int sourceDirectoryFileDescriptor,
        string sourceName,
        int destinationDirectoryFileDescriptor,
        string finalName)
    {
        if (MkdirAt(destinationDirectoryFileDescriptor, finalName, UnixDirectoryMode) != 0)
        {
            return Marshal.GetLastWin32Error();
        }

        if (RenameAtUnix(
                sourceDirectoryFileDescriptor,
                sourceName,
                destinationDirectoryFileDescriptor,
                finalName) != 0)
        {
            var error = Marshal.GetLastWin32Error();

            // Leave nothing behind: the reservation was this call's doing, and a stray
            // empty directory in the library is exactly the kind of debris a later scan
            // would have to reason about.
            UnlinkAt(destinationDirectoryFileDescriptor, finalName, AtRemovedirLinux);
            return error;
        }

        return 0;
    }

    /// <summary>
    /// The errnos a kernel returns for a rename flag the filesystem cannot honour, as
    /// opposed to one the caller got wrong. EINVAL is what FUSE reports.
    ///
    /// EXDEV is deliberately absent, though the move service treats it alongside these:
    /// a hard link cannot cross a device either, so routing it here would only trade one
    /// failure for a second. It stays an error, as it was.
    /// </summary>
    internal static bool IsUnsupportedRenameFlagError(int error) =>
        error == Einval || error == Enosys || error == Eopnotsupp;
    private static void RenameRelativeEntryWindows(
        SafeFileHandle directoryHandle,
        SafeFileHandle entryHandle,
        string finalName,
        bool replaceExisting)
    {
        var fileNameBytes = Encoding.Unicode.GetBytes(finalName);
        var rootDirectoryOffset = IntPtr.Size == 8 ? 8 : 4;
        var fileNameLengthOffset = rootDirectoryOffset + IntPtr.Size;
        var fileNameOffset = fileNameLengthOffset + sizeof(uint);
        var bufferSize = checked(fileNameOffset + fileNameBytes.Length);
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (var index = 0; index < bufferSize; index++)
            {
                Marshal.WriteByte(buffer, index, 0);
            }

            const int fileRenameInformation = 10;
            const int fileRenameInformationEx = 65;
            const int fileRenameReplaceIfExists = 0x00000001;
            const int fileRenamePosixSemantics = 0x00000002;
            if (replaceExisting)
            {
                Marshal.WriteInt32(
                    buffer,
                    0,
                    fileRenameReplaceIfExists | fileRenamePosixSemantics);
            }
            else
            {
                Marshal.WriteByte(buffer, 0, 0);
            }
            Marshal.WriteIntPtr(
                buffer,
                rootDirectoryOffset,
                directoryHandle.DangerousGetHandle());
            Marshal.WriteInt32(buffer, fileNameLengthOffset, fileNameBytes.Length);
            Marshal.Copy(fileNameBytes, 0, buffer + fileNameOffset, fileNameBytes.Length);
            var status = NtSetInformationFile(
                entryHandle,
                out _,
                buffer,
                checked((uint)bufferSize),
                replaceExisting
                    ? fileRenameInformationEx
                    : fileRenameInformation);
            if (status < 0)
            {
                var error = unchecked((int)RtlNtStatusToDosError(status));
                throw new Win32Exception(
                    error,
                    $"Could not publish a pinned filesystem entry relative to its owned directory (Windows error {error}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

}
