using System.ComponentModel;
using Listenarr.Domain.Audiobooks.Enumerations;

namespace Listenarr.Infrastructure.FileSystem;

public partial class FileMover
{
    /// <summary>
    /// Whether the destination this journal describes is present and usable.
    ///
    /// The file being at the path is the whole test. If someone replaced it by hand,
    /// through a share, or through a sync client, that replacement is what the library
    /// should hold - the app's job is to notice what is on disk, not to insist the disk
    /// still matches evidence it recorded a moment earlier.
    /// </summary>
    /// <remarks>
    /// This used to compare the destination's inode and its parent directory's inode
    /// against what the journal recorded, and refuse on any difference. On a pooled FUSE
    /// filesystem - unraid's shfs, where one directory is backed by both an array disk
    /// and a cache pool - the same directory reports different inodes depending on which
    /// pool answers. A verified 354MB encode was therefore rejected and deleted, and
    /// every filesystem operation was disabled until an operator edited the database.
    /// The evidence was never wrong about the bytes; it was wrong about what an inode
    /// means.
    /// </remarks>
    private static RegistrationPublicationMatchOutcome ProbeMarkerlessJournalTarget(
        FileMutationJournal journal,
        string targetPhysicalObjectIdentity)
    {
        _ = targetPhysicalObjectIdentity;

        try
        {
            var destination = Path.GetFullPath(journal.DestinationPath);
            var parent = Path.GetDirectoryName(destination);
            var fileName = Path.GetFileName(destination);
            if (string.IsNullOrWhiteSpace(parent)
                || string.IsNullOrWhiteSpace(fileName))
            {
                return RegistrationPublicationMatchOutcome.Mismatch;
            }

            // Still opened through a pinned, no-follow handle on the parent: that is what
            // stops a symlink swapped in mid-operation from redirecting the write
            // somewhere outside the library. It is a containment guarantee, not an
            // identity one, so it survives.
            using var anchor = PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(
                parent,
                createMissing: false);

            var parentVisibility = anchor.ProbeVisiblePathMatch();
            if (parentVisibility != RegistrationPublicationMatchOutcome.Match)
            {
                return parentVisibility;
            }

            var openOutcome = anchor.TryOpenExistingFileWithOutcome(
                fileName,
                requireDeleteAccess: false,
                out var entry);
            using (entry)
            {
                if (openOutcome == PinnedFileOpenOutcome.Unavailable)
                {
                    return RegistrationPublicationMatchOutcome.Unavailable;
                }
                if (openOutcome != PinnedFileOpenOutcome.Opened || entry == null)
                {
                    return RegistrationPublicationMatchOutcome.Mismatch;
                }

                // The path resolves to a real file inside the pinned parent. That is the
                // answer; nothing about its inode or its bytes is asked.
                return entry.ProbeVisiblePathMatch();
            }
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
            exception.NativeErrorCode is 2 or 3)
        {
            return RegistrationPublicationMatchOutcome.Mismatch;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or Win32Exception
                or PlatformNotSupportedException)
        {
            return RegistrationPublicationMatchOutcome.Unavailable;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException
                or NotSupportedException or PathTooLongException
                or System.Security.SecurityException)
        {
            return RegistrationPublicationMatchOutcome.Mismatch;
        }
    }

    private async Task ValidateMarkerlessRegistrationJournalAsync(
        FileMutationJournal journal,
        FileAction action,
        FileMoveGateLease gate,
        bool isCompanionFile,
        int? companionAudiobookId)
    {
        if (journal.ProtocolVersion != FileMutationProtocol.Current
            || journal.Action != action
            || journal.AudiobookFileId != (isCompanionFile
                ? FileMutationOwner.RegistrationCompanionFile
                : null)
            || (isCompanionFile
                && journal.AudiobookId != companionAudiobookId)
            || !await JournalPathsMatchGateAsync(journal, gate))
        {
            throw new InvalidOperationException(
                "The durable registration identity does not match the requested operation.");
        }
    }
}
