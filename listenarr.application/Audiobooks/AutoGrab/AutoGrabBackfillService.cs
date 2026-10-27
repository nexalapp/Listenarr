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
using Listenarr.Application.Audiobooks.Contracts;
using Listenarr.Application.Audiobooks.Contracts.Repositories;
using Listenarr.Application.Audiobooks.Suggestions;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.AutoGrab
{
    /// <param name="Considered">Books wanted with a release already found for them.</param>
    /// <param name="Sent">How many went to the download client.</param>
    /// <param name="Skipped">How many could not be sent, with the reason in the log.</param>
    public sealed record AutoGrabBackfillReport(
        int Considered,
        int Sent,
        int Skipped,
        IReadOnlyList<string> SentTitles);

    public interface IAutoGrabBackfillService
    {
        /// <summary>
        /// Sends the release already found for every monitored book that has no file and
        /// no download, up to <paramref name="limit"/>.
        /// </summary>
        Task<AutoGrabBackfillReport> RunAsync(int limit, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Catches up the books that were added but never downloaded.
    ///
    /// Adding a book and sending its release are two steps, and for a long while the
    /// second could fail without saying so - the add reported itself a success, the book
    /// sat monitored and empty, and nothing retried it until the six-hourly search came
    /// round. This walks that backlog deliberately.
    ///
    /// It costs the forum nothing it has not already paid: the posts behind these books
    /// have been thanked, so their payload is visible and the resolver reads it without
    /// pressing the button again. A book whose post was never thanked goes through the
    /// thank budget like any other.
    /// </summary>
    public sealed class AutoGrabBackfillService(
        ILibraryListService library,
        IAudiobookRepository audiobooks,
        ISuggestionReleaseService releases,
        ILogger<AutoGrabBackfillService> logger) : IAutoGrabBackfillService
    {
        public async Task<AutoGrabBackfillReport> RunAsync(
            int limit, CancellationToken cancellationToken = default)
        {
            var waiting = (await library.GetAllAsync())
                .Where(item => item.Wanted && !string.IsNullOrWhiteSpace(item.Asin))
                .ToList();

            var sent = new List<string>();
            var skipped = 0;

            foreach (var item in waiting.Take(Math.Max(1, limit)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var audiobook = await audiobooks.GetByIdAsync(item.Id);
                if (audiobook is null)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    if (await releases.GrabStoredAsync(audiobook, cancellationToken))
                    {
                        sent.Add(item.Title ?? item.Asin!);
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    // One book that will not send is not a reason to abandon the rest.
                    logger.LogWarning(
                        ex, "Backfill could not send '{Title}' ({Asin})", item.Title, item.Asin);
                    skipped++;
                }
            }

            logger.LogInformation(
                "Auto-grab backfill: {Considered} waiting, {Sent} sent, {Skipped} skipped",
                waiting.Count, sent.Count, skipped);

            return new AutoGrabBackfillReport(waiting.Count, sent.Count, skipped, sent);
        }
    }
}
