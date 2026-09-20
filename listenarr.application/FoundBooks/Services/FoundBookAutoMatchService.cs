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
using Listenarr.Application.Audiobooks.Transcription;
using Listenarr.Application.Common;
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.FoundBooks.Services
{
    /// <summary>
    /// The automatic identification. A row's tags are asked first; when they give no
    /// match or only a near one, the book's own opening is listened to once and the
    /// spoken credits are asked instead. The best answer is written to the row so the
    /// Found tab shows it on load and the automatic add can act on it.
    /// </summary>
    /// <remarks>
    /// A row is listened to at most once: whisper's answer does not change, and a book
    /// whose credits name nothing useful should not cost a minute of CPU every scan.
    /// A row that already carries a sure match, from a person or an earlier pass, is
    /// left alone.
    /// </remarks>
    public sealed class FoundBookAutoMatchService(
        IFoundBookRepository repository,
        IFoundBookCatalogueMatcher matcher,
        IFoundBookMatchService matches,
        ILogger<FoundBookAutoMatchService> logger) : IFoundBookAutoMatchService
    {
        /// <summary>The confidence written for a match the matcher calls beyond doubt.</summary>
        public const double SureConfidence = 1.0;

        /// <summary>The confidence written for the matcher's nearest-but-not-exact answer.</summary>
        public const double NearConfidence = 0.5;

        public async Task<FoundBookAutoMatchSummary> RunAsync(CancellationToken cancellationToken = default)
        {
            var notes = new List<string>();
            var rows = (await repository.GetAllAsync(cancellationToken))
                .Where(r => r.State == FoundBookState.Pending
                    && r.Completeness == FoundBookCompleteness.Complete
                    && r.LibraryStatus == FoundBookLibraryStatus.New
                    && r.MatchConfidence is not >= SureConfidence)
                .ToList();

            var matched = 0;
            var listened = 0;
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var (isMatched, didListen) = await IdentifyAsync(row, notes, cancellationToken);
                    if (isMatched)
                    {
                        matched++;
                    }

                    if (didListen)
                    {
                        listened++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && WorkerExceptionClassifier.IsNonFatal(ex))
                {
                    notes.Add($"{Describe(row)}: {ex.Message}");
                    logger.LogWarning(ex, "Automatic match failed for found book {Id}", row.Id);
                }
            }

            return new FoundBookAutoMatchSummary(rows.Count, matched, listened, notes);
        }

        private async Task<(bool Matched, bool Listened)> IdentifyAsync(FoundBook row, List<string> notes, CancellationToken cancellationToken)
        {
            var match = await matcher.MatchAsync(row, cancellationToken);
            var listened = false;
            if (match is not { HighConfidence: true } && row.HeardAt == null)
            {
                try
                {
                    var heard = await matches.ListenAsync(row.Id, cancellationToken);
                    listened = true;
                    if (heard != null && (heard.Credits.Title != null || heard.Credits.Author != null))
                    {
                        var refreshed = await repository.GetAsync(row.Id, cancellationToken);
                        if (refreshed != null)
                        {
                            match = await matcher.MatchAsync(refreshed, cancellationToken) ?? match;
                        }
                    }
                }
                catch (TranscriptionUnavailableException ex)
                {
                    // The model is still landing; the row keeps HeardAt null so the next
                    // scan listens.
                    notes.Add($"{Describe(row)}: {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    notes.Add($"{Describe(row)}: {ex.Message}");
                }
            }

            if (match == null)
            {
                return (false, listened);
            }

            if (!match.HighConfidence && !string.IsNullOrWhiteSpace(row.MatchAsin))
            {
                // A near answer does not replace what is already there.
                return (false, listened);
            }

            await matches.SetMatchAsync(row.Id, new FoundBookMatchChoice(
                match.Metadata.Asin,
                match.Metadata.Title,
                match.Metadata.Authors?.FirstOrDefault(),
                match.Metadata.Source,
                match.Metadata.ImageUrl,
                match.HighConfidence ? SureConfidence : NearConfidence), cancellationToken);
            logger.LogInformation(
                "Found book {Id} matched to {Asin}: {Reason}",
                row.Id,
                match.Metadata.Asin,
                match.Reason);
            return (true, listened);
        }

        private static string Describe(FoundBook row) =>
            string.IsNullOrWhiteSpace(row.DetectedTitle) ? row.BookFolder : row.DetectedTitle;
    }
}
