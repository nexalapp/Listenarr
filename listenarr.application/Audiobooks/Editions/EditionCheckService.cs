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
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Editions
{
    /// <inheritdoc cref="IEditionCheckService"/>
    public sealed class EditionCheckService(
        IAudiobookRepository audiobookRepository,
        ISearchService searchService,
        IAudiobookMetadataService metadataService,
        IFileSystem fileSystem,
        ILogger<EditionCheckService> logger) : IEditionCheckService
    {
        /// <summary>
        /// How many editions are looked up. A popular title returns dozens of results, most
        /// of them other books; each one costs a metadata fetch, and the right edition is
        /// never the fortieth.
        /// </summary>
        private const int MostEditionsToPrice = 12;

        /// <summary>How much of the record's title a result must carry to count as the same book.</summary>
        private const double SameBookThreshold = 0.6;

        /// <summary>
        /// How much of an author's name a result must carry to be the same author.
        ///
        /// <para>
        /// The author is what makes this safe. Titles collide constantly - this library
        /// holds several books called Thicker Than Blood, and a title-only filter matched
        /// one of them to a stranger's recording of another and reported it as the edition
        /// on disk. Two books sharing a title is ordinary; two sharing a title and an
        /// author is the same book.
        /// </para>
        /// </summary>
        private const double SameAuthorThreshold = 0.6;

        public async Task<EditionMatchResult> CheckAsync(int audiobookId, CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId)
                ?? throw new InvalidOperationException("That audiobook no longer exists.");

            var minutes = MeasuredMinutes(audiobook);
            if (minutes is null)
            {
                return EditionMatchResult.Nothing;
            }

            var editions = await EditionsOfAsync(audiobook, cancellationToken);
            if (editions.Count == 0)
            {
                return EditionMatchResult.Nothing;
            }

            var heard = string.IsNullOrWhiteSpace(audiobook.AudioAuditHeardNarrator)
                ? null
                : new[] { audiobook.AudioAuditHeardNarrator! };

            var result = EditionMatch.Judge(minutes, audiobook.Asin, editions, heard);
            logger.LogInformation(
                "Edition check of audiobook {AudiobookId}: {Outcome} against {Count} priced edition(s)",
                audiobookId,
                result.Outcome,
                editions.Count);
            return result;
        }

        /// <summary>
        /// How long the files run, or null when any of them has no measured duration. A
        /// partial sum reads as a short recording and would name the wrong edition.
        /// </summary>
        private double? MeasuredMinutes(Audiobook audiobook)
        {
            var total = 0.0;
            var counted = 0;
            foreach (var file in audiobook.Files ?? [])
            {
                if (!FileUtils.IsAudioFile(file.Path ?? string.Empty))
                {
                    continue;
                }

                var path = AudiobookFilePaths.ResolveFullPath(audiobook, file);
                if (path == null || !fileSystem.FileExists(path))
                {
                    continue;
                }

                if (file.DurationSeconds is not { } seconds || seconds <= 0)
                {
                    return null;
                }

                total += seconds;
                counted++;
            }

            return counted > 0 ? total / 60 : null;
        }

        /// <summary>
        /// Every edition of this book the catalogue will price, the record's own included.
        ///
        /// <para>
        /// The search gives identifiers but not runtimes, so each candidate is fetched for
        /// its length. That is the expensive half and the reason this runs on request
        /// rather than over a library.
        /// </para>
        /// </summary>
        private async Task<IReadOnlyList<EditionCandidate>> EditionsOfAsync(
            Audiobook audiobook,
            CancellationToken cancellationToken)
        {
            var title = audiobook.Title?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return [];
            }

            var asins = new List<string>();
            if (!string.IsNullOrWhiteSpace(audiobook.Asin))
            {
                asins.Add(audiobook.Asin!.Trim());
            }

            // A library catalogue publishes the runtime in the result itself, so those
            // editions are already priced and cost nothing further. A shop does not, and
            // has to be asked per edition below.
            var priced = new List<EditionCandidate>();

            try
            {
                var found = await searchService.IntelligentSearchAsync(
                    $"TITLE:{title}",
                    ct: cancellationToken);
                var authors = audiobook.Authors ?? [];
                foreach (var result in found)
                {
                    if (!AudioIdentityMatcher.SameTitle(title, result.Title, SameBookThreshold)
                        || !SameAuthor(authors, result.Artist))
                    {
                        continue;
                    }

                    if (result.Runtime is > 0)
                    {
                        priced.Add(new EditionCandidate(
                            result.Asin?.Trim() ?? result.Id ?? string.Empty,
                            result.Title,
                            Names(result.Narrator),
                            result.Publisher,
                            result.Runtime));
                        continue;
                    }

                    var asin = result.Asin?.Trim();
                    if (!string.IsNullOrWhiteSpace(asin)
                        && !asins.Contains(asin, StringComparer.OrdinalIgnoreCase)
                        && asins.Count < MostEditionsToPrice)
                    {
                        asins.Add(asin);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not list editions of {Title}", title);
            }

            var editions = new List<EditionCandidate>(priced);
            foreach (var asin in asins)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var metadata = await metadataService.GetAudibleMetadataAsync(asin);
                    if (metadata?.LengthMinutes is not > 0)
                    {
                        continue;
                    }

                    editions.Add(new EditionCandidate(
                        asin,
                        metadata.Title,
                        (metadata.Narrators ?? []).Select(n => n.Name ?? string.Empty).Where(n => n.Length > 0).ToList(),
                        metadata.Publisher,
                        metadata.LengthMinutes));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Could not price edition {Asin}", asin);
                }
            }

            return editions;
        }

        /// <summary>
        /// Whether a result is credited to one of the record's authors. A result that names
        /// nobody is allowed through, because a catalogue that omits the author is not
        /// evidence of a different one; a result that names someone else is not.
        /// </summary>
        private static bool SameAuthor(IReadOnlyList<string> authors, string? credited)
        {
            if (authors.Count == 0 || string.IsNullOrWhiteSpace(credited))
            {
                return true;
            }

            return authors.Any(author => AudioIdentityMatcher.SameTitle(author, credited, SameAuthorThreshold));
        }

        /// <summary>A comma-separated credit as the list of people it names.</summary>
        private static IReadOnlyList<string> Names(string? credit) =>
            (credit ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
    }
}
