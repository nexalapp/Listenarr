using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Scanning;

public partial class ScanJobProcessor
{
    private async Task RunSuccessfulPostCompletionEffectsAsync(
        ScanJob job,
        Audiobook audiobook,
        int found,
        int created,
        CancellationToken cancellationToken)
    {
        await NotifyAvailableAsync(audiobook, created);
        await QueuePostImportWorkAsync(audiobook, cancellationToken);
        try
        {
            if (_audiobookUpdatePublisher != null)
            {
                await _audiobookUpdatePublisher.PublishCurrentAsync(
                    audiobook.Id,
                    cancellationToken);
            }

            await _hubContext.Clients.All.SendAsync("ScanJobUpdate", new
            {
                jobId = job.Id.ToString(),
                audiobookId = job.AudiobookId,
                status = "Completed",
                found,
                created,
                completedAt = _timeProvider.GetUtcNow().UtcDateTime
            }, cancellationToken);
            _logger.LogInformation(
                "Broadcasted AudiobookUpdate for AudiobookId {AudiobookId} after scan job {JobId}",
                audiobook.Id,
                job.Id);
        }
        catch (Exception broadcastException) when (WorkerExceptionClassifier.IsNonFatal(broadcastException))
        {
            _logger.LogWarning(
                broadcastException,
                "Scan job {JobId} completed durably but its client update could not be broadcast",
                job.Id);
        }
    }

    private async Task BroadcastFailedScanAsync(
        ScanJob job,
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ScanJobUpdate", new
            {
                jobId = job.Id.ToString(),
                audiobookId = job.AudiobookId,
                status,
                error = ScanJobPublicError.FromInternal(error),
                failedAt = _timeProvider.GetUtcNow().UtcDateTime
            }, cancellationToken);
        }
        catch (Exception broadcastException) when (WorkerExceptionClassifier.IsNonFatal(broadcastException))
        {
            _logger.LogDebug(
                broadcastException,
                "Unable to broadcast terminal scan state for job {JobId}",
                job.Id);
        }
    }

    /// <summary>
    /// Offer the newly scanned book to the conversion and tag-writing queues.
    ///
    /// <para>
    /// This is the one hook both import paths reach. The download path and the
    /// manual/library path share no import service — the manual controller composes its
    /// own dependencies and never touches IDownloadImportService — but both finish by
    /// enqueueing a focused scan, so scan completion is where they converge, and it runs
    /// once per book rather than once per file.
    /// </para>
    /// <para>
    /// Conversion is offered first, and a book it accepts is not also offered for
    /// tagging: a conversion writes the tags itself, from the same mapping, as part of
    /// producing the file. Queueing both would mean rewriting a file that was written
    /// correctly seconds earlier.
    /// </para>
    /// <para>
    /// A post-completion effect: the scan is already durably complete, so a refusal or a
    /// failure here must not disturb it.
    /// </para>
    /// </summary>
    private async Task QueuePostImportWorkAsync(
        Audiobook audiobook,
        CancellationToken cancellationToken)
    {
        var converting = await QueueConversionIfWantedAsync(audiobook, cancellationToken);
        if (!converting)
        {
            await QueueTagWriteIfWantedAsync(audiobook, cancellationToken);
        }
    }

    /// <summary>
    /// Returns true when a conversion was queued, which means the book's file does not
    /// exist yet and the conversion will write its tags.
    /// </summary>
    private async Task<bool> QueueConversionIfWantedAsync(
        Audiobook audiobook,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var conversionQueue = scope.ServiceProvider
                .GetRequiredService<IConversionQueueService>();

            var result = await conversionQueue.EnqueueAsync(
                audiobook.Id,
                ConversionTrigger.Automatic,
                cancellationToken);

            if (result.Queued)
            {
                _logger.LogInformation(
                    "Queued conversion {JobId} for audiobook {AudiobookId} after scan",
                    result.JobId,
                    audiobook.Id);
                return true;
            }

            // A book already queued for conversion is still being converted, so the same
            // reasoning applies: leave its tags to that job.
            if (result.Outcome == ConversionEnqueueOutcome.AlreadyQueued)
            {
                return true;
            }

            if (result.Outcome
                is not ConversionEnqueueOutcome.Disabled
                and not ConversionEnqueueOutcome.NothingToConvert)
            {
                // Disabled and NothingToConvert are the ordinary answers for most books
                // and would be noise; anything else is worth a line.
                _logger.LogInformation(
                    "Did not queue a conversion for audiobook {AudiobookId}: {Reason}",
                    audiobook.Id,
                    result.Reason);
            }

            return false;
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            _logger.LogWarning(
                exception,
                "Could not offer audiobook {AudiobookId} to the conversion queue after its scan",
                audiobook.Id);
            return false;
        }
    }

    /// <summary>
    /// Offer the newly scanned book for tag writing, so an M4B that has just landed —
    /// from a download import or a manual import — carries the library's metadata rather
    /// than whatever the release shipped with.
    ///
    /// Cheap when there is nothing to do: the worker reads the file's current tags,
    /// finds them already correct, and rewrites nothing. That is what makes running this
    /// on every import reasonable rather than reckless.
    /// </summary>
    private async Task QueueTagWriteIfWantedAsync(
        Audiobook audiobook,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var tagQueue = scope.ServiceProvider.GetRequiredService<ITagQueueService>();

            var result = await tagQueue.EnqueueAsync(
                audiobook.Id,
                TagTrigger.Automatic,
                selectedTags: null,
                cancellationToken);

            if (result.Queued)
            {
                _logger.LogInformation(
                    "Queued tag write {JobId} for audiobook {AudiobookId} after scan",
                    result.JobId,
                    audiobook.Id);
            }
            else if (result.Outcome
                     is not TagEnqueueOutcome.Disabled
                     and not TagEnqueueOutcome.NothingToTag)
            {
                _logger.LogInformation(
                    "Did not queue a tag write for audiobook {AudiobookId}: {Reason}",
                    audiobook.Id,
                    result.Reason);
            }
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            _logger.LogWarning(
                exception,
                "Could not offer audiobook {AudiobookId} to the tag-writing queue after its scan",
                audiobook.Id);
        }
    }

    private async Task BroadcastFilesRemovedAsync(
        int audiobookId,
        IReadOnlyCollection<object> removed,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(
                "FilesRemoved",
                new { audiobookId, removed },
                cancellationToken);
        }
        catch (Exception broadcastException) when (WorkerExceptionClassifier.IsNonFatal(broadcastException))
        {
            _logger.LogDebug(
                broadcastException,
                "Failed to broadcast FilesRemoved event for audiobook {AudiobookId}",
                audiobookId);
        }
    }
}
