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

using Listenarr.Application.Common;
using Listenarr.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission
{
    public partial class DownloadService(
        IAudiobookRepository audiobookRepository,
        IConfigurationService configurationService,
        IDownloadRepository downloadRepository,
        ILogger<DownloadService> logger,
        IQualityProfileService qualityProfileService,
        ISearchService searchService,
        IDownloadClientGateway clientGateway,
        IDownloadQueueService downloadQueueService,
        INotificationService notificationService,
        IHubBroadcaster hubBroadcaster,
        IDownloadHistoryService downloadHistoryService,
        DownloadClientSelector downloadClientSelector,
        DownloadCachedTorrentStore cachedTorrentStore,
        IDownloadSubmissionPreparer submissionPreparer,
        DirectDownloadWorkflow directDownloadWorkflow,
        DownloadRemovalWorkflow downloadRemovalWorkflow,
        IReleaseBlocklistRepository releaseBlocklistRepository,
        ISecretProtector? secretProtector = null) : IDownloadService
    {
        // Cache expiration constants
        private const int QueueCacheExpirationSeconds = 10;
        private const int ClientStatusCacheExpirationSeconds = 30;
        private const int DirectDownloadTimeoutHours = 2;

        // Track qBittorrent sync state for incremental updates (clientId -> last rid)
        private readonly Dictionary<string, int> _qbittorrentSyncState = new();

        // Track qBittorrent torrent cache for merging incremental updates (clientId -> (torrentHash -> QueueItem))
        private readonly Dictionary<string, Dictionary<string, QueueItem>> _qbittorrentTorrentCache = new();
        public async Task<string> StartDownloadAsync(SearchResult searchResult, string downloadClientId, int? audiobookId = null)
        {
            return await SendToDownloadClientAsync(searchResult, downloadClientId, audiobookId);
        }

        /// <summary>
        /// Retrieve cached torrent bytes and filename for a given download id if available
        /// </summary>
        public Task<(byte[]? Bytes, string? FileName)> GetCachedTorrentAsync(string downloadId)
        {
            return cachedTorrentStore.GetCachedTorrentAsync(downloadId);
        }

        /// <summary>
        /// Retrieve cached announce URLs for a given download id if available
        /// </summary>
        public Task<List<string>?> GetCachedAnnouncesAsync(string downloadId)
        {
            return cachedTorrentStore.GetCachedAnnouncesAsync(downloadId);
        }

        public async Task<(bool Success, string Message, DownloadClientConfiguration? Client)> TestDownloadClientAsync(DownloadClientConfiguration client)
        {
            if (client == null)
            {
                return (false, "Download client configuration not provided", null);
            }

            try
            {
                var (success, message) = await clientGateway.TestConnectionAsync(client);
                return (success, message, client);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Error during TestDownloadClientAsync for client {ClientId}", LogRedaction.SanitizeText(client.Id ?? client.Name ?? client.Type));
                return (false, ex.Message, client);
            }
        }

        public async Task<string> SendToDownloadClientAsync(SearchResult searchResult, string? downloadClientId = null, int? audiobookId = null)
        {
            return await SendToDownloadClientAsync(
                TrustedDownloadCandidateFactory.Create(searchResult),
                downloadClientId,
                audiobookId);
        }

        public async Task<string> SendToDownloadClientAsync(
            TrustedDownloadCandidate candidate,
            string? downloadClientId = null,
            int? audiobookId = null)
        {
            logger.LogInformation(
                "Preparing trusted download '{Title}' using protocol {Protocol}, AudiobookId: {AudiobookId}",
                LogRedaction.SanitizeText(candidate.Title),
                candidate.SourceDescriptor.Protocol,
                audiobookId);

            var downloadId = Guid.NewGuid().ToString();
            if (audiobookId is int audiobookIdValue && audiobookIdValue > 0)
            {
                try
                {
                    if (await DownloadDuplicateGuard.HasActiveDownloadAsync(
                            audiobookIdValue,
                            configurationService,
                            downloadRepository))
                    {
                        logger.LogInformation(
                            "Skipping duplicate download for audiobook {AudiobookId} — an active download already exists. Title: '{Title}'",
                            audiobookIdValue,
                            candidate.Title);
                        return string.Empty;
                    }
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
                {
                    logger.LogDebug(
                        exception,
                        "Failed to check for duplicate downloads for audiobook {AudiobookId} (non-blocking)",
                        audiobookIdValue);
                }
            }

            var prepared = await submissionPreparer.PrepareAsync(candidate, downloadId);
            if (prepared is PreparedDirectDownloadSubmission directDownload)
            {
                logger.LogInformation("Processing trusted direct download for: {Title}", candidate.Title);
                return await directDownloadWorkflow.CreateTrackedDownloadAsync(directDownload, audiobookId);
            }

            var isTorrent = prepared.Protocol == DownloadProtocol.Torrent;

            logger.LogInformation(
                "Processing as {DownloadType} after server-side validation for '{Title}'",
                prepared.Protocol,
                candidate.Title);

            if (downloadClientId == null)
            {
                downloadClientId = await downloadClientSelector.GetAppropriateDownloadClientAsync(isTorrent);

                if (downloadClientId == null)
                {
                    var clientType = isTorrent ? "torrent" : "NZB";
                    var neededClients = isTorrent ? "qBittorrent or Transmission" : "SABnzbd or NZBGet";

                    // A typed conflict, not a bare Exception. This is the operator's own
                    // configuration, and the message says exactly what to do about it - but
                    // a bare Exception reaches the controller's catch-all as a 500, and
                    // ServerErrorProblemDetailsFilter rewrites every 5xx body outside
                    // Development to "Internal server error" with no detail. The advice was
                    // being written and then thrown away on the way out.
                    throw new ApplicationConflictException(
                        "download_client_unavailable",
                        $"No {clientType} download client is enabled, so this release cannot be sent anywhere. "
                            + $"Add and enable {neededClients} under Settings > Download Clients, then try again.");
                }

                logger.LogInformation("Auto-selected download client {ClientId} for {ClientType}", downloadClientId, isTorrent ? "torrent" : "NZB");
            }

            return await SubmitPreparedAsync(downloadId, candidate, prepared, downloadClientId, audiobookId, isTorrent);
        }

        /// <summary>
        /// Submits an NZB found inside a finished Usenet download as the download it points
        /// to. Some uploaders post the real release under obfuscated names and an NZB of it
        /// under the forum's search code, so grabbing the code fetches only that NZB. It goes
        /// to the same client, for the same book, with the archive password the outer grab
        /// was given.
        /// </summary>
        public async Task<string> SendNestedNzbAsync(Download outer, string fileName, byte[] nzbBytes)
        {
            ArgumentNullException.ThrowIfNull(outer);
            ArgumentNullException.ThrowIfNull(nzbBytes);
            if (string.IsNullOrWhiteSpace(outer.DownloadClientId))
            {
                throw new DownloadClientSubmissionException("The finished download has no client to send its NZB to.");
            }

            var protectedPassword = outer.GetMetadataString(DownloadMetadataKeys.ProtectedArchivePassword);
            var password = string.IsNullOrWhiteSpace(protectedPassword) || secretProtector == null
                ? null
                : secretProtector.Unprotect(protectedPassword);
            var source = outer.GetMetadataString("Source") ?? "Nested NZB";
            var quality = outer.GetMetadataString("Quality");
            var locator = $"nested-nzb:{outer.Id}/{fileName}";

            var candidate = new TrustedDownloadCandidate(
                locator,
                outer.Title ?? fileName,
                outer.Artist ?? string.Empty,
                outer.Album ?? string.Empty,
                source,
                quality,
                outer.Language,
                0,
                null,
                new DownloadSourceDescriptor(
                    null,
                    null,
                    DownloadProtocol.Usenet,
                    [new DownloadSourceLocator(DownloadSourceLocatorKind.ReleaseId, locator)]));
            var prepared = new PreparedUsenetSubmission(
                candidate.Title,
                candidate.Artist,
                candidate.Album,
                source,
                quality,
                outer.Language,
                0,
                locator,
                nzbBytes,
                fileName,
                password);

            logger.LogInformation(
                "Submitting the NZB found inside download {DownloadId} ('{FileName}') as the release it points to",
                outer.Id,
                fileName);
            var downloadId = await SubmitPreparedAsync(
                Guid.NewGuid().ToString(),
                candidate,
                prepared,
                outer.DownloadClientId,
                outer.AudiobookId,
                isTorrent: false);

            var continued = await downloadRepository.FindAsync(downloadId);
            if (continued != null)
            {
                continued.Metadata ??= new Dictionary<string, object>();
                continued.Metadata[DownloadMetadataKeys.ContinuesFrom] = outer.Id;
                await UpdateAsync(continued);
            }

            return downloadId;
        }

        /// <summary>
        /// Records a prepared download, hands it to the download client, and announces it.
        /// Shared by an ordinary grab and by an NZB found inside a finished download, so the
        /// two are tracked, recorded and shown the same way.
        /// </summary>
        private async Task<string> SubmitPreparedAsync(
            string downloadId,
            TrustedDownloadCandidate candidate,
            PreparedDownloadSubmission prepared,
            string downloadClientId,
            int? audiobookId,
            bool isTorrent)
        {
            var downloadClient = await configurationService.GetDownloadClientConfigurationAsync(downloadClientId);
            if (downloadClient == null || !downloadClient.IsEnabled)
            {
                throw new Exception("Download client not found or disabled");
            }

            logger.LogInformation("Sending to {ClientType} download client: {ClientName}", downloadClient.Type, downloadClient.Name);

            // Ensure downloadClientId is non-null before assignment into model
            var downloadClientIdForModel = downloadClientId ?? string.Empty;

            // Create Download record in database before sending to client
            var download = DownloadRecordFactory.CreateQueuedDownload(
                downloadId,
                candidate,
                prepared,
                downloadClient,
                downloadClientIdForModel,
                audiobookId);

            // Kept with the download, protected, because a finished download can turn out to
            // be an NZB of the real release, and its archives need the same password.
            if (prepared is PreparedUsenetSubmission { Password: { Length: > 0 } password } && secretProtector != null)
            {
                download.Metadata[DownloadMetadataKeys.ProtectedArchivePassword] = secretProtector.Protect(password);
            }

            try
            {
                await downloadRepository.AddAsync(download);
            }
            catch (UniqueConstraintViolationException) when (audiobookId.HasValue)
            {
                logger.LogInformation(
                    "Concurrent duplicate download prevented for audiobook {AudiobookId}",
                    audiobookId);
                return string.Empty;
            }
            logger.LogInformation("Created download record in database: {DownloadId} for '{Title}'", downloadId, candidate.Title);

            DownloadClientSubmissionResult submissionResult;
            try
            {
                submissionResult = await clientGateway.AddAsync(downloadClient, prepared);
                if (submissionResult == null || string.IsNullOrWhiteSpace(submissionResult.ExternalId))
                {
                    throw new DownloadClientSubmissionException("The download client did not return a verified download identifier.");
                }
            }
            catch (OperationCanceledException)
            {
                await RemoveProvisionalDownloadAsync(downloadId);
                throw;
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                await RemoveProvisionalDownloadAsync(downloadId);

                if (exception is DownloadClientSubmissionException)
                {
                    throw;
                }

                throw new DownloadClientSubmissionException("Failed to send the torrent to the download client.", exception);
            }

            var downloadToUpdate = await downloadRepository.FindAsync(downloadId);
            if (downloadToUpdate != null)
            {
                DownloadClientMetadataUpdater.ApplyClientSpecificId(downloadToUpdate, downloadClient, submissionResult.ExternalId);
                await UpdateAsync(downloadToUpdate);
                logger.LogInformation("Updated download {DownloadId} with client-specific ID: {ClientId}", downloadId, submissionResult.ExternalId);
            }

            // Record history only after the external client accepted the download.
            if (!string.IsNullOrEmpty(downloadClientIdForModel))
            {
                try
                {
                    var protocol = isTorrent ? DownloadProtocol.Torrent : DownloadProtocol.Usenet;
                    await downloadHistoryService.RecordGrabbedAsync(
                        downloadId,
                        downloadClientIdForModel,
                        candidate.Title,
                        protocol);
                    logger.LogInformation("Recorded grabbed event in history for download {DownloadId}", downloadId);
                }
                catch (Exception histEx) when (histEx is not OperationCanceledException && histEx is not OutOfMemoryException && histEx is not StackOverflowException)
                {
                    logger.LogWarning(histEx, "Failed to record grabbed event in history for download {DownloadId} (non-critical)", downloadId);
                }
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            var notificationData = await DownloadNotificationPayloadBuilder.BuildBookDownloadingPayloadAsync(
                audiobookRepository,
                audiobookId,
                downloadId,
                ToSearchResult(candidate, prepared),
                downloadClient);

            await notificationService.SendNotificationAsync("book-downloading", notificationData, settings.WebhookUrl, settings.EnabledNotificationTriggers);

            // Trigger an immediate realtime queue update so the UI shows the new download right away
            // Add a small delay to allow the download client to process and index the new download
            try
            {
                logger.LogInformation("Waiting briefly for download client to process new download...");
                await Task.Delay(1500); // Give qBittorrent/other clients time to index the torrent

                logger.LogInformation("Triggering immediate queue update after sending download to client");
                var currentQueueSnapshot = await downloadQueueService.GetQueueSnapshotAsync();
                await hubBroadcaster.BroadcastQueueUpdateAsync(currentQueueSnapshot);
                logger.LogInformation("Immediate queue update sent with {Count} items via IHubBroadcaster", currentQueueSnapshot?.Items?.Count ?? 0);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Failed to trigger immediate queue update (non-fatal)");
            }

            return downloadId;
        }

        private async Task RemoveProvisionalDownloadAsync(string downloadId)
        {
            try
            {
                await downloadRepository.RemoveAsync(downloadId);
                logger.LogInformation("Removed provisional download {DownloadId} after client submission failed", downloadId);
            }
            catch (Exception cleanupException) when (cleanupException is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                logger.LogError(cleanupException, "Failed to remove provisional download {DownloadId} after client submission failure", downloadId);
            }
        }

        public async Task<bool> RemoveFromQueueAsync(string downloadId, string? downloadClientId = null, bool force = false)
        {
            return await downloadRemovalWorkflow.RemoveAsync(downloadId, downloadClientId, force);
        }

        //
        // Helper stubs added to satisfy callers while refactor completes.
        // These are conservative, safe no-op / simple implementations.
        //

        private static SearchResult ToSearchResult(
            TrustedDownloadCandidate candidate,
            PreparedDownloadSubmission prepared)
            => new()
            {
                Id = candidate.Id,
                Title = candidate.Title,
                Artist = candidate.Artist,
                Album = candidate.Album,
                Source = candidate.Source,
                Quality = candidate.Quality,
                Language = candidate.Language,
                Size = candidate.Size,
                Seeders = candidate.Seeders,
                DownloadType = prepared.Protocol.ToString()
            };

        private async Task LogDownloadHistory(Audiobook audiobook, string source, SearchResult result)
        {
            // Placeholder: log to internal logger for visibility; actual history persistence is elsewhere
            try
            {
                logger.LogInformation("LogDownloadHistory: audiobook={Title}, source={Source}, result={ResultTitle}", audiobook?.Title, source, result?.Title);
            }
            catch (Exception caughtEx_13) when (caughtEx_13 is not OperationCanceledException && caughtEx_13 is not OutOfMemoryException && caughtEx_13 is not StackOverflowException)
            {
                System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
            }
            await Task.CompletedTask;
        }

        public async Task UpdateAsync(Download download)
        {
            var previous = await downloadRepository.GetByIdAsync(download.Id);
            if (previous is null)
            {
                logger.LogWarning("Skipping update for unknown download {DownloadId}", LogRedaction.SanitizeText(download.Id));
                return;
            }

            await downloadRepository.UpdateAsync(download);

            switch (previous.Status, download.Status)
            {
                case var (old, next) when old == next:
                    return;
                case (_, DownloadStatus.Moved):
                    await notificationService.OnDownloadImportedAsync(download);
                    return;
                case (_, DownloadStatus.Failed):
                case (_, DownloadStatus.ImportBlocked):
                    await notificationService.OnDownloadFailedAsync(download);
                    return;
                default:
                    return;
            }
        }
    }
}
