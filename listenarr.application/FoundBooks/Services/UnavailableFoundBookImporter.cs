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
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Domain.FoundBooks;

namespace Listenarr.Application.FoundBooks.Services
{
    /// <summary>The importer a host registers when it has no manual-import workflow to offer.</summary>
    public sealed class UnavailableFoundBookImporter : IFoundBookImporter
    {
        public bool IsAvailable => false;

        public Task<FoundBookImportOutcome> ImportAsync(
            FoundBook row,
            int audiobookId,
            bool includeCompanions,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FoundBookImportOutcome(false, 0, 0, "No importer is available in this host."));
    }
}
