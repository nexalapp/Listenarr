/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Downloads.Import
{
    /// <summary>
    /// Puts right the downloads whose import finished but whose record never caught up.
    ///
    /// An import is committed in one transaction - the files are registered, the job is
    /// completed, the ImportCommitted checkpoint is set and the download is marked Moved.
    /// A second job for the same download could then write ImportPending back over it,
    /// and the download sat in Activity forever as work that was already done. The write
    /// is guarded now, but a guard only helps the next one: the rows already stranded,
    /// and anything a future race or a crash between those writes leaves behind, need a
    /// way back without someone editing the database by hand.
    ///
    /// So this asks a question with an unambiguous answer - did this download's import
    /// commit? - and if it did, says so on the record. It never decides that an import
    /// happened; it only repeats what the import itself already recorded.
    /// </summary>
    public class StalledImportRecoveryBackgroundService(
        ILogger<StalledImportRecoveryBackgroundService> logger,
        IStalledImportRecoveryProcessor processor,
        IWorkerCycleRunner cycleRunner) : BackgroundService
    {
        /// <summary>
        /// Long enough that it is never mistaken for the thing doing the importing, short
        /// enough that a stranded download is corrected while its owner is still watching.
        /// </summary>
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Let the import pipeline start first. A download mid-import is not stalled, and
        /// waking into the middle of one only invites the race this exists to clean up.
        /// </summary>
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("StalledImportRecoveryService started");

            await cycleRunner.RunPeriodicAsync(
                nameof(StalledImportRecoveryBackgroundService),
                initialDelay: InitialDelay,
                intervalProvider: () => Interval,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("StalledImportRecoveryService stopped");
        }
    }

    public interface IStalledImportRecoveryProcessor
    {
        Task RunCycleAsync(CancellationToken cancellationToken);
    }

    public class StalledImportRecoveryProcessor(
        ILogger<StalledImportRecoveryProcessor> logger,
        IServiceScopeFactory serviceScopeFactory) : IStalledImportRecoveryProcessor
    {
        /// <summary>
        /// The checkpoint the import writes last, in the same transaction that marks the
        /// download Moved. Its presence on a completed job is proof the import finished.
        /// </summary>
        internal const string ImportCommittedCheckpoint = "ImportCommitted";

        /// <summary>When this download was last nudged, so a client that ignores us is not asked forever.</summary>
        internal const string LastResumeNudgeKey = "LastResumeNudgeAt";

        /// <summary>
        /// How long a paused download is left alone before it is nudged, and how long
        /// before it may be nudged again. Long enough that a download genuinely pausing
        /// for a moment is never touched, short enough to clear within an hour.
        /// </summary>
        internal static readonly TimeSpan PausedGrace = TimeSpan.FromMinutes(30);

        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceScopeFactory.CreateScope();
            var downloadRepository = scope.ServiceProvider.GetRequiredService<IDownloadRepository>();
            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();
            var jobService = scope.ServiceProvider.GetRequiredService<IDownloadProcessingJobService>();

            var awaiting = await downloadRepository.GetAwaitingImportAsync(cancellationToken);
            if (awaiting.Count == 0)
            {
                return;
            }

            var recovered = 0;
            var nudged = 0;
            foreach (var download in awaiting)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // A download paused at the client never commits an import, so the
                    // question below has no answer for it. It is stuck for a different
                    // reason and wants a different remedy.
                    if (download.Status == DownloadStatus.Paused)
                    {
                        if (await NudgePausedDownloadAsync(scope, download, cancellationToken))
                        {
                            nudged++;
                        }

                        continue;
                    }

                    if (!await ImportAlreadyCommittedAsync(jobService, download.Id))
                    {
                        continue;
                    }

                    // Unblock first: a download that reached ImportBlocked carries the
                    // reason it was blocked, and leaving that on a record we are about to
                    // call imported would tell two stories at once. Unblock() routes
                    // through Importing(), so Imported() has to follow it, not precede it.
                    if (download.Status == DownloadStatus.ImportBlocked)
                    {
                        download.Unblock();
                    }

                    download.Imported();
                    await downloadService.UpdateAsync(download);
                    recovered++;

                    logger.LogInformation(
                        "Recovered stalled import for '{Title}' ({DownloadId}): its import had already committed, so the download is marked as imported",
                        download.Title,
                        download.Id);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    // One unrecoverable row must not stop the rest from being put right.
                    logger.LogWarning(
                        ex,
                        "Could not recover stalled import for '{Title}' ({DownloadId})",
                        download.Title,
                        download.Id);
                }
            }

            if (recovered > 0 || nudged > 0)
            {
                logger.LogInformation(
                    "Stalled import recovery corrected {Recovered} and resumed {Nudged} of {Candidates} download(s) awaiting import",
                    recovered,
                    nudged,
                    awaiting.Count);
            }
        }

        /// <summary>
        /// Asks the client to resume a download that has sat paused long enough to be
        /// stuck rather than merely pausing.
        ///
        /// Listenarr never pauses its own downloads - it submits them unpaused and has no
        /// pause of its own - so a paused download is the client's doing. The case this
        /// exists for is NZBGet holding par2 volumes paused: when the content finishes,
        /// every file left in the group is paused, so the group reads as paused, is never
        /// post-processed, and the finished download never becomes one Listenarr can
        /// import. Resuming is what lets the client finish it.
        ///
        /// The nudge is recorded so a client that ignores it is not asked on every cycle.
        /// </summary>
        private async Task<bool> NudgePausedDownloadAsync(
            IServiceScope scope,
            Download download,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            if (now - download.StartedAt < PausedGrace)
            {
                return false;
            }

            if (DateTime.TryParse(
                    download.GetMetadataString(LastResumeNudgeKey),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var lastNudge)
                && now - lastNudge < PausedGrace)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(download.DownloadClientId))
            {
                return false;
            }

            var configurationService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var client = await configurationService.GetDownloadClientConfigurationAsync(download.DownloadClientId);
            if (client == null)
            {
                return false;
            }

            var gateway = scope.ServiceProvider.GetRequiredService<IDownloadClientGateway>();
            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();

            // Record the attempt whatever comes back: a client that refuses must not be
            // asked again on the next cycle, and one that accepts needs no second ask.
            download.SetMetadata(LastResumeNudgeKey, now.ToString("O", CultureInfo.InvariantCulture));
            var resumed = await gateway.ResumeAsync(client, download, cancellationToken);
            await downloadService.UpdateAsync(download);

            if (resumed)
            {
                logger.LogInformation(
                    "Resumed '{Title}' ({DownloadId}): it had been paused at the client since {StartedAt:u} with nothing left to do but be resumed",
                    download.Title,
                    download.Id,
                    download.StartedAt);
            }

            return resumed;
        }

        /// <summary>
        /// Whether this download's import already finished: a completed processing job
        /// carrying the checkpoint the import writes as it commits.
        ///
        /// Both halves matter. A job that carries the checkpoint but is not complete is
        /// still running, and a completed job without it failed or was reset - neither is
        /// a download that has been imported.
        /// </summary>
        private static async Task<bool> ImportAlreadyCommittedAsync(
            IDownloadProcessingJobService jobService,
            string downloadId)
        {
            var jobs = await jobService.GetJobsForDownloadAsync(downloadId);
            return jobs.Any(job =>
                job.Status == ProcessingJobStatus.Completed
                && job.HasCheckpoint(ImportCommittedCheckpoint));
        }
    }
}
