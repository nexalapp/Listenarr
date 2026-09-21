using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Listenarr.Infrastructure.FileSystem;

internal sealed partial class PinnedDirectoryCreation
{
    private static void CreateRelativeHardLinkWindows(
        SafeFileHandle sourceHandle,
        SafeFileHandle destinationDirectoryHandle,
        string destinationName)
    {
        var fileNameBytes = System.Text.Encoding.Unicode.GetBytes(destinationName);
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

            Marshal.WriteByte(buffer, 0, 0);
            Marshal.WriteIntPtr(
                buffer,
                rootDirectoryOffset,
                destinationDirectoryHandle.DangerousGetHandle());
            Marshal.WriteInt32(
                buffer,
                fileNameLengthOffset,
                fileNameBytes.Length);
            Marshal.Copy(
                fileNameBytes,
                0,
                buffer + fileNameOffset,
                fileNameBytes.Length);
            const int fileLinkInformation = 11;
            var status = NtSetInformationFile(
                sourceHandle,
                out _,
                buffer,
                checked((uint)bufferSize),
                fileLinkInformation);
            if (status < 0)
            {
                var error = unchecked((int)RtlNtStatusToDosError(status));
                throw new Win32Exception(
                    error,
                    $"Could not create a hardlink relative to the pinned destination directory (Windows error {error}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeFileHandle CreateRelativeReadWriteFileUnix(
        SafeFileHandle parentHandle,
        string fileName)
    {
        var fd = OpenAt(
            parentHandle.DangerousGetHandle().ToInt32(),
            fileName,
            UnixOpenFlags.CreateReadWriteExclusiveNoFollow(),
            UnixFileMode);
        if (fd >= 0)
        {
            return new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        }

        // The errno is the whole diagnosis - EACCES is a folder somebody else owns,
        // EEXIST a leftover, ENOSPC a full disk - so it goes in the message the
        // operator reads, not only in the code they cannot see.
        var createError = Marshal.GetLastWin32Error();
        throw new Win32Exception(
            createError,
            $"Could not create '{fileName}' in the pinned destination directory: {Marshal.GetPInvokeErrorMessage(createError)} (errno {createError}).");
    }

    private static SafeFileHandle OpenRelativeFileUnix(
        SafeFileHandle parentHandle,
        string fileName,
        string fullPath)
    {
        var fd = OpenAt(
            parentHandle.DangerousGetHandle().ToInt32(),
            fileName,
            UnixOpenFlags.OpenReadNoFollow(),
            mode: 0);
        if (fd >= 0)
        {
            return new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        }

        var openError = Marshal.GetLastWin32Error();
        throw new Win32Exception(
            openError,
            $"Could not open pinned file '{fullPath}': {Marshal.GetPInvokeErrorMessage(openError)} (errno {openError}).");
    }

    private static SafeFileHandle OpenRelativeFileForWriteUnix(
        SafeFileHandle parentHandle,
        string fileName,
        string fullPath,
        bool readable = false)
    {
        var fd = OpenAt(
            parentHandle.DangerousGetHandle().ToInt32(),
            fileName,
            readable
                ? UnixOpenFlags.OpenReadWriteNoFollow()
                : UnixOpenFlags.OpenWriteNoFollow(),
            mode: 0);
        if (fd >= 0)
        {
            return new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        }

        throw new Win32Exception(
            Marshal.GetLastWin32Error(),
            $"Could not open pinned file for writing '{fullPath}'.");
    }

    private static void EnsureFileHandleIsNotReparsePoint(
        SafeFileHandle handle,
        string path)
    {
        if (!GetFileAttributeTagInformationByHandleEx(
                handle,
                FileInformationClass.FileAttributeTagInfo,
                out var information,
                (uint)Marshal.SizeOf<FileAttributeTagInformation>()))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Could not inspect pinned file '{path}'.");
        }

        if ((information.FileAttributes & FileAttributeReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "A pinned file cannot be a symbolic link or reparse point.");
        }
    }
}
