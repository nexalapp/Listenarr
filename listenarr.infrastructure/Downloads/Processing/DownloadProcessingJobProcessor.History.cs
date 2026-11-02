/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text.Json;

namespace Listenarr.Infrastructure.Downloads.Processing
{
    public partial class DownloadProcessingJobProcessor
    {
        /// <summary>
        /// How long an import will wait for a client that has the bytes but has not
        /// finished with them. Generous, because unpacking and repairing a long book takes
        /// minutes, and the cost of waiting is nothing - but bounded, so a client that
        /// never reports a final location fails honestly instead of waiting for ever.
        /// </summary>
        internal static readonly TimeSpan ClientFinishingWindow = TimeSpan.FromHours(2);

        /// <summary>How long between asking the client again.</summary>
        internal static readonly TimeSpan ClientFinishingPoll = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Whether an empty source means "not yet" rather than "nothing".
        ///
        /// A client that has finished with a download gives a final location for it; while
        /// it is still unpacking, the only path it offers is its own working directory, and
        /// finding no audio there says nothing about the download. The absence of that
        /// final location is the signal, which keeps this free of any particular client's
        /// directory names.
        /// </summary>
        internal static bool SourceIsNotReadyYet(QueueItem queueItem, DownloadProcessingJob job, DateTime now) =>
            queueItem != null
            && job != null
            && string.IsNullOrWhiteSpace(queueItem.ContentPath)
            && now - job.CreatedAt < ClientFinishingWindow;

        /// <summary>
        /// Puts the import back in the queue without spending an attempt, and says so in
        /// the history so a book waiting on its client does not look like one failing.
        /// </summary>
        private static async Task WaitForClientAsync(
            DownloadProcessingJob job,
            IDownloadProcessingJobService jobService,
            IHistoryRepository historyRepository,
            Download download,
            Audiobook audiobook,
            string correlationId,
            CancellationToken ct)
        {
            const string reason =
                "The download client has not finished with this download yet - it reports no final "
                + "location, so there is nothing to import from. Waiting rather than counting an attempt.";
            job.WaitForClient(reason, ClientFinishingPoll);
            await jobService.UpdateJobAsync(job);
            await RecordHistoryAsync(
                historyRepository,
                download,
                audiobook,
                HistoryEvents.ImportRetry,
                HistoryOutcome.Retrying,
                correlationId,
                reason,
                new Dictionary<string, object>
                {
                    ["JobId"] = job.Id,
                    ["RetryCount"] = job.RetryCount,
                    ["WaitingForClient"] = true
                },
                ct);
        }

        private static async Task ScheduleRetryAsync(
            DownloadProcessingJob job,
            IDownloadProcessingJobService jobService,
            IHistoryRepository historyRepository,
            Download download,
            Audiobook audiobook,
            string correlationId,
            string reason,
            CancellationToken ct)
        {
            job.ScheduleRetry(reason);
            await jobService.UpdateJobAsync(job);
            var exhausted = job.Status == ProcessingJobStatus.Failed;
            await RecordHistoryAsync(
                historyRepository,
                download,
                audiobook,
                exhausted ? HistoryEvents.ImportFailed : HistoryEvents.ImportRetry,
                exhausted ? HistoryOutcome.Failed : HistoryOutcome.Retrying,
                correlationId,
                reason,
                new Dictionary<string, object>
                {
                    ["JobId"] = job.Id,
                    ["RetryCount"] = job.RetryCount
                },
                ct);
        }

        private static async Task FailImportAsync(
            DownloadProcessingJob job,
            IDownloadProcessingJobService jobService,
            IHistoryRepository historyRepository,
            Download download,
            Audiobook audiobook,
            string correlationId,
            string reason,
            CancellationToken ct)
        {
            await jobService.UpdateJobAsync(job.MarkAsFailed(reason));
            await RecordHistoryAsync(
                historyRepository,
                download,
                audiobook,
                HistoryEvents.ImportFailed,
                HistoryOutcome.Failed,
                correlationId,
                reason,
                new Dictionary<string, object>
                {
                    ["JobId"] = job.Id,
                    ["RetryCount"] = job.RetryCount
                },
                ct);
        }

        private static Task RecordHistoryAsync(
            IHistoryRepository historyRepository,
            Download download,
            Audiobook audiobook,
            string eventType,
            HistoryOutcome outcome,
            string correlationId,
            string message,
            Dictionary<string, object> details,
            CancellationToken ct) =>
            historyRepository.AddAsync(new History
            {
                AudiobookId = audiobook.Id,
                AudiobookTitle = audiobook.Title,
                SourceTitle = download.Title,
                DownloadId = download.Id.ToUpperInvariant(),
                DownloadClientId = download.DownloadClientId,
                EventType = eventType,
                Outcome = outcome,
                Source = "DownloadImport",
                Message = message,
                Error = outcome == HistoryOutcome.Failed ? message : null,
                Timestamp = DateTime.UtcNow,
                CorrelationId = correlationId,
                Data = JsonSerializer.Serialize(details)
            }, ct);
    }
}
