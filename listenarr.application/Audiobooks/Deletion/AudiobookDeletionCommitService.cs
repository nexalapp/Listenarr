using Listenarr.Application.Audiobooks.Conversion;
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Application.Common;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Deletion;

/// <summary>
/// Centralizes the cancellation-to-commit transition for audiobook deletion.
/// </summary>
public sealed class AudiobookDeletionCommitService(
    IAudiobookRepository repository,
    IConversionQueueService conversionQueue,
    ITagQueueService tagQueue,
    IConversionJobRepository conversionJobs,
    ITagJobRepository tagJobs,
    ILogger<AudiobookDeletionCommitService> logger) : IAudiobookDeletionCommitService
{
    public Task<AudiobookDeletionCommitResult> DeleteAsync(
        int id,
        CancellationToken requestCancellationToken = default) =>
        DeleteAsync(
            id,
            includeFiles: false,
            requestCancellationToken);

    public async Task<AudiobookDeletionCommitResult> DeleteAsync(
        int id,
        bool includeFiles,
        CancellationToken requestCancellationToken = default)
    {
        requestCancellationToken.ThrowIfCancellationRequested();
        var audiobook = await repository.GetForUpdateSnapshotAsync(
            id,
            requestCancellationToken);
        if (audiobook == null)
        {
            return new AudiobookDeletionCommitResult(
                AudiobookDeletionCommitOutcome.NotFound,
                null);
        }

        if (includeFiles && RequiresTrackedFileSnapshot(audiobook))
        {
            audiobook = await repository.GetByIdSnapshotAsync(
                id,
                requestCancellationToken);
            if (audiobook == null)
            {
                return new AudiobookDeletionCommitResult(
                    AudiobookDeletionCommitOutcome.NotFound,
                    null);
            }
        }

        // A job still working on the book is stopped before the book goes: a worker
        // that went on encoding would be publishing into a folder the delete had
        // just removed.
        await CancelActiveJobsAsync(id, requestCancellationToken);

        // This is the single request-cancellation fence for the irreversible
        // database deletion. IAudiobookRepository.DeleteByIdAsync is intentionally
        // non-request-cancelable, so a disconnect after this point cannot leave
        // the workflow pretending that a committed delete was rolled back.
        RequestCancellationBoundary.EnterNonCancelablePhase(
            requestCancellationToken);

        var deleted = await repository.DeleteByIdAsync(id);
        if (deleted)
        {
            // The book's jobs go with it, terminal ones included, so the Activity
            // page does not go on listing "Audiobook #1192" for a record that no
            // longer exists. What a cascading key would do, done here: SQLite cannot
            // take one on an existing table without a rebuild.
            var removedJobs = await conversionJobs.DeleteForAudiobookAsync(id)
                + await tagJobs.DeleteForAudiobookAsync(id);
            if (removedJobs > 0)
            {
                logger.LogInformation("Removed {Count} job row(s) of deleted audiobook {AudiobookId}", removedJobs, id);
            }
        }

        return new AudiobookDeletionCommitResult(
            deleted
                ? AudiobookDeletionCommitOutcome.Deleted
                : AudiobookDeletionCommitOutcome.Failed,
            audiobook);
    }

    private async Task CancelActiveJobsAsync(int id, CancellationToken cancellationToken)
    {
        var conversion = await conversionQueue.GetActiveJobForAudiobookAsync(id, cancellationToken);
        if (conversion != null)
        {
            var result = await conversionQueue.CancelAsync(conversion.Id, cancellationToken);
            logger.LogInformation(
                "Cancelled conversion {JobId} of audiobook {AudiobookId} ahead of its deletion: {Outcome}",
                conversion.Id,
                id,
                result.Outcome);
        }

        var tagging = await tagQueue.GetActiveJobForAudiobookAsync(id, cancellationToken);
        if (tagging != null)
        {
            var result = await tagQueue.CancelAsync(tagging.Id, cancellationToken);
            logger.LogInformation(
                "Cancelled tag job {JobId} of audiobook {AudiobookId} ahead of its deletion: {Outcome}",
                tagging.Id,
                id,
                result.Outcome);
        }
    }

    private static bool RequiresTrackedFileSnapshot(Audiobook audiobook)
    {
        var boundaryPath = !string.IsNullOrWhiteSpace(audiobook.BasePath)
            ? audiobook.BasePath
            : audiobook.FilePath;
        return string.IsNullOrWhiteSpace(boundaryPath)
            || FileSystemPathIdentity.TryCanonicalizeUnambiguousStoredAbsolutePathForHost(
                boundaryPath,
                out _,
                out _);
    }
}
