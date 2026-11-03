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

using Listenarr.Application.Audiobooks.Suggestions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Downloads.Monitoring
{
    /// <summary>
    /// What happens when a download client reports a failure.
    ///
    /// Split out from the monitor because it is a decision of its own rather than part of
    /// polling: the client's word is classified, and the classification decides whether
    /// the release is set aside, whether the client's record of the attempt has to be
    /// deleted, and whether another release is worth looking for at once. Keeping it
    /// beside the polling loop also pushed that file past the size
    /// BackendArchitectureTests enforces.
    /// </summary>
    public partial class DownloadMonitorProcessor
    {
        private async Task OnDownloadFailed(
            Download download,
            DownloadClientConfiguration client,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var downloadClientGateway = scope.ServiceProvider.GetRequiredService<IDownloadClientGateway>();
            var audiobookRepository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var downloadHistoryService = scope.ServiceProvider.GetRequiredService<IDownloadHistoryService>();
            var configurationService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var settings = await configurationService.GetApplicationSettingsAsync();
            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();

            await downloadHistoryService.RecordDownloadFailedAsync(
                download.Id,
                download.DownloadClientId,
                download.Title ?? "Unknown",
                errorMessage);

            if (!settings.FailedDownloadHandlingEnabled)
            {
                return;
            }

            var clientItemId = download.GetExternalId();
            // Every client but NZBGet has its failed item removed here and kept no
            // longer. NZBGet's history is read for final paths and failure detail, so it
            // is left in place unless the failure is one whose record would discard the
            // next grab - see the purge below, which the policy decides.
            if (!string.IsNullOrWhiteSpace(clientItemId) &&
                ShouldRemoveFailedClientItem(client))
            {
                await downloadClientGateway.RemoveAsync(client, clientItemId, deleteFiles: false, cancellationToken);
            }

            var reason = ResolveFailureReason(client, download, errorMessage);
            var response = DownloadFailurePolicy.For(reason);

            if (response.BlockRelease)
            {
                await BlockFailedReleaseAsync(scope, download, reason, errorMessage, cancellationToken);
            }

            // A record of the attempt the client will not let go of is the reason the next
            // grab is thrown away unread. It has to go before the search, or the release
            // the search finds is discarded as a copy of this one.
            if (response.PurgeClientRecord && !string.IsNullOrWhiteSpace(clientItemId))
            {
                await PurgeFailedClientRecordAsync(downloadClientGateway, client, download, clientItemId, cancellationToken);
            }

            if (!settings.FailedDownloadAutoSearch || !download.AudiobookId.HasValue)
            {
                return;
            }

            if (!response.SearchAgain)
            {
                logger.LogInformation(
                    "Not searching again after the {Reason} failure of download {DownloadId}: another release would fail in the same place",
                    reason,
                    download.Id);
                return;
            }

            try
            {
                var audiobook = await audiobookRepository.GetByIdAsync(download.AudiobookId.Value);
                if (audiobook == null || !audiobook.Monitored)
                {
                    return;
                }

                if (await HasExhaustedDailyAttemptsAsync(scope, download.AudiobookId.Value, cancellationToken))
                {
                    logger.LogWarning(
                        "Not searching again for '{Title}': {Limit} automatic grabs have already failed for it today",
                        LogRedaction.SanitizeText(audiobook.Title),
                        DownloadFailurePolicy.MaxAutomaticAttemptsPerDay);
                    return;
                }

                var replacement = await downloadService.SearchAndDownloadAsync(download.AudiobookId.Value);
                if (replacement.NothingObtainable)
                {
                    await RecordUnobtainableAsync(scope, audiobook, cancellationToken);
                    await RetireAutomaticFailureAsync(scope, download, audiobook, cancellationToken);
                }

                // A replacement that was grabbed retires this row on its own, where the
                // new download is recorded - the same rule, applied at the only place
                // that knows a successor exists.
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Failed to auto-search after failed download {DownloadId}", download.Id);
            }
        }

        /// <summary>
        /// Records that no indexer holds a release for this book, after a failure whose
        /// re-search came back with nothing it could use.
        ///
        /// This is the other half of searching again. Without it a book whose only
        /// release is unrepairable kept a failed row for ever: the search ran once,
        /// found that the one posting was the one just blocked, and stopped - leaving a
        /// row in the queue that nothing would ever clear and no record anywhere that
        /// the book cannot currently be had. The stored answer is what the Wanted and
        /// Suggested pages read, and it comes due again after UnobtainableRecheckDays,
        /// so this is a statement about now rather than for ever.
        /// </summary>
        private async Task RecordUnobtainableAsync(
            IServiceScope scope,
            Audiobook audiobook,
            CancellationToken cancellationToken)
        {
            var asin = audiobook.Asin?.Trim();
            if (string.IsNullOrWhiteSpace(asin))
            {
                return;
            }

            try
            {
                var store = scope.ServiceProvider.GetRequiredService<ISuggestionReleaseRepository>();
                var stored = await store.GetAsync(asin, cancellationToken) ?? new SuggestionRelease { Asin = asin };
                stored.Status = SuggestionReleaseStatus.NotFound;
                stored.CheckedAt = DateTime.UtcNow;
                stored.Detail = "Every indexer was asked after a failed grab and none held a release this book could use.";
                // The release it used to point at is the one that just failed and is now
                // blocked, so keeping any of it would offer the book back its own failure.
                stored.IndexerName = null;
                stored.ReleaseTitle = null;
                stored.SizeBytes = null;
                stored.ProtectedResult = null;
                stored.ProtectedNzbUrl = null;
                stored.Preparation = ReleasePreparationState.NotPrepared;
                stored.PreparationDetail = null;
                stored.PreparedAt = null;
                stored.PayloadFingerprint = null;
                stored.ResolvedVia = null;
                await store.SaveAsync(stored, cancellationToken);

                logger.LogInformation(
                    "No indexer holds a release for '{Title}' that it has not already lost; recorded as unobtainable for now",
                    LogRedaction.SanitizeText(audiobook.Title));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Could not record audiobook {AudiobookId} as unobtainable", audiobook.Id);
            }
        }

        /// <summary>
        /// Takes an automatic grab's failure out of the queue once there is nothing left
        /// to try for it.
        ///
        /// The queue is for work in hand and for the books this library was asked to
        /// find. A grab nobody asked for has now said everything it can - the release is
        /// blocked, the book is recorded as unobtainable - and leaving the row behind
        /// only buries the failures that do want attention. A grab made by hand stays,
        /// because whoever made it is tracking that book; so does a row from before the
        /// provenance was recorded, which is the safe way to be wrong.
        /// </summary>
        private async Task RetireAutomaticFailureAsync(
            IServiceScope scope,
            Download download,
            Audiobook audiobook,
            CancellationToken cancellationToken)
        {
            if (!download.GetMetadataBool(DownloadMetadataKeys.AutomaticGrab))
            {
                return;
            }

            try
            {
                await scope.ServiceProvider
                    .GetRequiredService<IDownloadRepository>()
                    .RemoveAsync(download.Id);
                logger.LogInformation(
                    "Retired the failed automatic grab for '{Title}': nothing else is on offer, and the book is recorded as unobtainable",
                    LogRedaction.SanitizeText(audiobook.Title));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Could not retire the failed automatic grab {DownloadId}", download.Id);
            }

            await Task.CompletedTask;
        }

        internal static bool ShouldRemoveFailedClientItem(DownloadClientConfiguration client)
        {
            return !string.Equals(client.Type, "nzbget", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// What kind of failure this was.
        ///
        /// The classification is made where the client is read, so the download usually
        /// carries it already. The raw status is consulted as well because a download
        /// that failed before the classification existed has none, and because it is the
        /// only source when the failure arrives by a path that did not go through the
        /// queue mapping.
        /// </summary>
        internal static DownloadFailureReason ResolveFailureReason(
            DownloadClientConfiguration client,
            Download download,
            string errorMessage)
        {
            if (download.FailureReason is { } recorded && recorded != DownloadFailureReason.Unknown)
            {
                return recorded;
            }

            if (string.Equals(client.Type, "nzbget", StringComparison.OrdinalIgnoreCase))
            {
                return NzbgetFailureMessageMapper.Classify(
                    download.GetMetadataString("ClientFailureReason") ?? errorMessage);
            }

            // Any other client said something in its own words, and nothing has been
            // taught to read them. Unknown is the reason that blocks nothing.
            return DownloadFailureReason.Unknown;
        }

        /// <summary>
        /// Records that this release must not be grabbed for this book again.
        ///
        /// A download with no book behind it has nothing to block it for - the search that
        /// would read the block is per book - so it is left alone.
        /// </summary>
        private async Task BlockFailedReleaseAsync(
            IServiceScope scope,
            Download download,
            DownloadFailureReason reason,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            if (!download.AudiobookId.HasValue)
            {
                return;
            }

            try
            {
                var blocklist = scope.ServiceProvider.GetRequiredService<IReleaseBlocklistRepository>();
                await blocklist.AddAsync(
                    BlockedRelease.For(download, reason, errorMessage),
                    cancellationToken);
                logger.LogInformation(
                    "Blocked release '{Title}' for audiobook {AudiobookId} after a {Reason} failure; the next search will pass over it",
                    LogRedaction.SanitizeText(download.Title),
                    download.AudiobookId.Value,
                    reason);

                await MarkStoredReleaseStaleAsync(scope, download.AudiobookId.Value, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                // Better to search again without the block than to abandon the book: the
                // worst case is one wasted grab of the release that just failed.
                logger.LogWarning(ex, "Could not block the failed release for download {DownloadId}", download.Id);
            }
        }

        /// <summary>
        /// Makes the stored availability answer due again, so the book is offered a
        /// different release instead of waiting out the thirty days a found release is
        /// normally trusted for.
        ///
        /// The check stores one release per book. Blocking that release without this left
        /// the book holding a pointer to something it may no longer grab, and nothing to
        /// replace it until the next monthly pass - which is how a book could fail and
        /// then sit idle for weeks. The re-check reads the blocklist now, so what it
        /// stores next is a release this book has not already lost.
        /// </summary>
        private async Task MarkStoredReleaseStaleAsync(
            IServiceScope scope,
            int audiobookId,
            CancellationToken cancellationToken)
        {
            try
            {
                var audiobook = await scope.ServiceProvider
                    .GetRequiredService<IAudiobookRepository>()
                    .GetByIdAsync(audiobookId);
                var asin = audiobook?.Asin?.Trim();
                if (string.IsNullOrWhiteSpace(asin))
                {
                    return;
                }

                var store = scope.ServiceProvider.GetRequiredService<ISuggestionReleaseRepository>();
                var stored = await store.GetAsync(asin, cancellationToken);
                if (stored == null)
                {
                    return;
                }

                // Dated to the epoch rather than deleted: the row is the record of what was
                // found, and the worker's "is this due" question only reads the date.
                stored.CheckedAt = DateTime.MinValue;
                await store.SaveAsync(stored, cancellationToken);
                logger.LogInformation(
                    "The stored release for '{Title}' ({Asin}) was blocked, so its availability check is due again",
                    audiobook!.Title,
                    asin);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(
                    ex,
                    "Could not make the stored release for audiobook {AudiobookId} due again",
                    audiobookId);
            }
        }

        /// <summary>
        /// Deletes the client's own record of a failed attempt, finally - for NZBGet that
        /// means HistoryFinalDelete, because an ordinary delete leaves the entry in the
        /// hidden duplicate history where it goes on discarding later grabs.
        ///
        /// What is lost is NZBGet's copy of a failure Listenarr has already recorded: the
        /// reason is on the download, the message is in Listenarr's history, and a blocked
        /// release carries both. What is gained is that the next release gets fetched
        /// instead of being thrown away as a copy of this one.
        /// </summary>
        private async Task PurgeFailedClientRecordAsync(
            IDownloadClientGateway gateway,
            DownloadClientConfiguration client,
            Download download,
            string clientItemId,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(client.Type, "nzbget", StringComparison.OrdinalIgnoreCase))
            {
                // Other clients have no hidden duplicate history, and the ordinary
                // removal above has already run for them.
                return;
            }

            try
            {
                await gateway.RemoveAsync(client, clientItemId, deleteFiles: true, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(
                    ex,
                    "Could not delete the client's record of failed download {DownloadId}; a later grab may be discarded as its duplicate",
                    download.Id);
            }
        }

        /// <summary>
        /// Whether this book has already lost its allowance of automatic grabs today.
        ///
        /// Each failure blocks a release, so the search does move on rather than repeat
        /// itself - but a book posted badly a dozen times over should not spend a night
        /// working through all of them. Counting the book's own failed downloads is enough
        /// and needs no state of its own.
        /// </summary>
        private static async Task<bool> HasExhaustedDailyAttemptsAsync(
            IServiceScope scope,
            int audiobookId,
            CancellationToken cancellationToken)
        {
            var downloads = scope.ServiceProvider.GetRequiredService<IDownloadRepository>();
            var since = DateTime.UtcNow.AddDays(-1);
            var recent = await downloads.GetByAudiobookIdAsync(audiobookId, cancellationToken);
            return recent.Count(entry =>
                entry.Status == DownloadStatus.Failed &&
                entry.StartedAt >= since) >= DownloadFailurePolicy.MaxAutomaticAttemptsPerDay;
        }
    }
}
