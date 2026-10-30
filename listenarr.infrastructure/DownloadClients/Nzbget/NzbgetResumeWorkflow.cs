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

using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.DownloadClients.Nzbget
{
    /// <summary>
    /// Resumes a paused NZBGet group.
    ///
    /// NZBGet holds par2 recovery volumes paused and fetches them only if a repair is
    /// needed. When the content files finish, every file still in the group is paused, so
    /// NZBGet reports the whole group as paused, never post-processes it, and it sits in
    /// the queue indefinitely - the download is done, but it never becomes a download that
    /// Listenarr can import. Resuming the group is what lets NZBGet finish it: observed on
    /// a stalled book, the resume was followed within a second by post-unpack renaming and
    /// the collection being added to history.
    /// </summary>
    internal sealed class NzbgetResumeWorkflow
    {
        private readonly NzbgetXmlRpcClient _xmlRpcClient;
        private readonly ILogger _logger;

        public NzbgetResumeWorkflow(NzbgetXmlRpcClient xmlRpcClient, ILogger logger)
        {
            _xmlRpcClient = xmlRpcClient;
            _logger = logger;
        }

        public async Task<bool> ResumeAsync(DownloadClientConfiguration client, string id, CancellationToken ct = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var numericId = NzbgetRequestPlanner.TryParseId(id);
            if (!numericId.HasValue)
            {
                _logger.LogWarning(
                    "NZBGet resume skipped because item id {ItemId} is not a numeric NZBGet id",
                    LogRedaction.SanitizeText(id));

                return false;
            }

            try
            {
                var result = await _xmlRpcClient.CallAsync(
                    client,
                    "editqueue",
                    "GroupResume",
                    0,
                    string.Empty,
                    new[] { numericId.Value });

                var success = result.Element("boolean")?.Value == "1";
                if (success)
                {
                    _logger.LogInformation("Resumed paused NZBGet group {Id}", LogRedaction.SanitizeText(id));
                }
                else
                {
                    _logger.LogDebug("NZBGet declined to resume group {Id}", LogRedaction.SanitizeText(id));
                }

                return success;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to resume NZBGet group {Id}", LogRedaction.SanitizeText(id));
                return false;
            }
        }
    }
}
