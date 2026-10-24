/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.Extensions.DependencyInjection;

namespace Listenarr.Infrastructure.Downloads.Processing
{
    public partial class DownloadProcessingJobProcessor
    {
        /// <summary>
        /// True when a finished Usenet download holds nothing but NZB files.
        ///
        /// Some uploaders post the real release under obfuscated names and an NZB of it under
        /// the forum post's search code, so grabbing the code fetches only that NZB. Imported,
        /// it registers no audio and blocks; it is the next download, not a book.
        /// </summary>
        internal static bool IsOnlyNzbFiles(IReadOnlyCollection<string> files) =>
            files.Count > 0
            && files.All(file => string.Equals(Path.GetExtension(file), ".nzb", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Submits the NZB inside a finished download as the download it points to, and
        /// closes this one as continued rather than failed. The outer download is closed
        /// first: a book has one active download, and the continuation is it.
        /// </summary>
        private static async Task ContinueAsNestedNzbAsync(
            DownloadProcessingJob job,
            IServiceProvider services,
            IDownloadRepository downloadRepository,
            IDownloadProcessingJobService jobService,
            IHistoryRepository historyRepository,
            Download download,
            Audiobook audiobook,
            IReadOnlyList<string> nzbFiles,
            string correlationId,
            CancellationToken cancellationToken)
        {
            if (nzbFiles.Count != 1)
            {
                await FailImportAsync(job, jobService, historyRepository, download, audiobook, correlationId,
                    $"The download holds {nzbFiles.Count} NZB files and no audio; it needs a person to choose which to fetch.",
                    cancellationToken);
                return;
            }

            var nzbPath = nzbFiles[0];
            var fileName = Path.GetFileName(nzbPath);
            var bytes = await File.ReadAllBytesAsync(nzbPath, cancellationToken);

            download.Status = DownloadStatus.Moved;
            await downloadRepository.UpdateAsync(download);

            string continuedAs;
            try
            {
                continuedAs = await services.GetRequiredService<IDownloadService>()
                    .SendNestedNzbAsync(download, fileName, bytes);
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                download.Status = DownloadStatus.ImportPending;
                await downloadRepository.UpdateAsync(download);
                await FailImportAsync(job, jobService, historyRepository, download, audiobook, correlationId,
                    $"The download is an NZB of the release ('{fileName}'), and submitting it failed: {exception.Message}",
                    cancellationToken);
                return;
            }

            if (string.IsNullOrEmpty(continuedAs))
            {
                // The service answers with no id when the book already has an active download.
                download.Status = DownloadStatus.ImportPending;
                await downloadRepository.UpdateAsync(download);
                await FailImportAsync(job, jobService, historyRepository, download, audiobook, correlationId,
                    $"The download is an NZB of the release ('{fileName}'), but the book already has an active download.",
                    cancellationToken);
                return;
            }

            download.Metadata ??= new Dictionary<string, object>();
            download.Metadata[DownloadMetadataKeys.ContinuedAs] = continuedAs;
            await downloadRepository.UpdateAsync(download);

            job.AddLogEntry($"The download held only '{fileName}', an NZB of the release; submitted it as download {continuedAs}");
            await jobService.UpdateJobAsync(job.MarkAsCompleted());
            await RecordHistoryAsync(
                historyRepository,
                download,
                audiobook,
                HistoryEvents.Grabbed,
                HistoryOutcome.Succeeded,
                correlationId,
                $"The download was an NZB of the release ('{fileName}'); fetching what it points to",
                new Dictionary<string, object>
                {
                    ["JobId"] = job.Id,
                    ["ContinuedAs"] = continuedAs
                },
                cancellationToken);
        }
    }
}
