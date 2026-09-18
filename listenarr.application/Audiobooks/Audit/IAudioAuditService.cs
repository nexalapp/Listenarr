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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Domain.Audiobooks.Audit;

namespace Listenarr.Application.Audiobooks.Audit
{
    /// <summary>
    /// Listens to a book's opening and closing and records whether they agree with
    /// the record. Runs on the tag queue so it never contends with itself for the CPU.
    /// </summary>
    public interface IAudioAuditService
    {
        /// <summary>Queue an audit, or say why not.</summary>
        Task<TagEnqueueResult> EnqueueAsync(int audiobookId, TagTrigger trigger, CancellationToken cancellationToken = default);

        /// <summary>Listen and judge now, on the worker. Records the outcome on the book.</summary>
        Task<AudioAuditResult> AuditAsync(int audiobookId, CancellationToken cancellationToken = default);
    }
}
