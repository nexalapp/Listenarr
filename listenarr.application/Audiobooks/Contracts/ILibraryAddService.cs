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

namespace Listenarr.Application.Audiobooks.Contracts
{
    public interface ILibraryAddService
    {
        Task<LibraryAddOperationResult> AddToLibraryAsync(
            LibraryAddOperationRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed class LibraryAddOperationRequest
    {
        public AudibleBookMetadata Metadata { get; set; } = new();

        public bool Monitored { get; set; } = true;

        public int? QualityProfileId { get; set; }

        public bool AutoSearch { get; set; }

        public string? DestinationPath { get; set; }

        /// <summary>
        /// Adds the book even when the library already holds an edition that looks
        /// identical. One product identifier can legitimately describe more than one
        /// book - a collection ASIN covers every novella in it - and a record can be
        /// wrong about which book it holds. Neither case should be able to lock a real
        /// file out of the library permanently, so the caller may state that this is a
        /// distinct book and be believed.
        /// </summary>
        public bool AllowDuplicateEdition { get; set; }

        public SearchResult? SearchResult { get; set; }

        public string HistorySource { get; set; } = "AddNew";

        public string? HistoryMessage { get; set; }
    }

    public sealed class LibraryAddOperationResult
    {
        public bool Added { get; set; }

        public bool AlreadyExists { get; set; }

        public bool ValidationFailed { get; set; }

        public string Message { get; set; } = string.Empty;

        public string? ValidationMessage { get; set; }

        public string? ValidationCode { get; set; }

        public string? ValidationField { get; set; }

        public string? ResolvedDestination { get; set; }

        public Audiobook? Audiobook { get; set; }
    }
}
