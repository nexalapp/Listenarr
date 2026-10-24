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
using Listenarr.Domain.Audiobooks.Quality;

namespace Listenarr.Application.Audiobooks.Quality
{
    /// <summary>
    /// Judges what a book's recording sounds like by decoding a couple of minutes of it.
    /// Runs on the tag queue; a probe is seconds of CPU, so it rides the planning lane
    /// rather than holding up a write.
    /// </summary>
    public interface IAudioQualityService
    {
        /// <summary>Queue a probe, or say why not.</summary>
        Task<TagEnqueueResult> EnqueueAsync(int audiobookId, TagTrigger trigger, CancellationToken cancellationToken = default);

        /// <summary>Probe now, on the worker, and record the verdict on the book.</summary>
        Task<AudioQualityReport> ProbeAsync(int audiobookId, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    }
}
