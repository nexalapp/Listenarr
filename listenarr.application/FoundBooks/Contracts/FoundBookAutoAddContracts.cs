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
using Listenarr.Domain.FoundBooks;

namespace Listenarr.Application.FoundBooks.Contracts
{
    /// <summary>What the catalogue says a found book is, and how sure the match is.</summary>
    public sealed record FoundBookCatalogueMatch(AudibleBookMetadata Metadata, bool HighConfidence, string Reason);

    /// <summary>
    /// Finds the catalogue record for a found book without a person in the loop. High
    /// confidence means an ASIN carried in the files' own tags, or a title that agrees
    /// exactly with an author that agrees; anything looser is offered to a person, never
    /// added by itself.
    /// </summary>
    public interface IFoundBookCatalogueMatcher
    {
        Task<FoundBookCatalogueMatch?> MatchAsync(FoundBook row, CancellationToken cancellationToken = default);
    }

    public sealed record FoundBookImportOutcome(bool Success, int ImportedCount, int TotalCount, string? Error);

    /// <summary>
    /// Moves a found book's audio into the library record it was matched to. Implemented
    /// by the host over the manual-import workflow; a host without one gets a
    /// null-object that reports the import as unavailable.
    /// </summary>
    public interface IFoundBookImporter
    {
        bool IsAvailable { get; }

        Task<FoundBookImportOutcome> ImportAsync(
            FoundBook row,
            int audiobookId,
            bool includeCompanions,
            CancellationToken cancellationToken = default);
    }

    public sealed record FoundBookAutoAddSummary(int Considered, int Added, int Skipped, IReadOnlyList<string> Notes);

    /// <summary>
    /// Adds every offered, complete, new book whose catalogue match is beyond doubt.
    /// Runs after each scan when the setting is on.
    /// </summary>
    public interface IFoundBookAutoAddService
    {
        Task<FoundBookAutoAddSummary> RunAsync(CancellationToken cancellationToken = default);
    }
}
