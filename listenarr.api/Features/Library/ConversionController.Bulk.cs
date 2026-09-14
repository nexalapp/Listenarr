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
using Listenarr.Domain.Audiobooks.Conversion;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    /// <summary>The books to queue. Ids the caller repeats are queued once.</summary>
    public sealed class BulkConvertRequest
    {
        public List<int> AudiobookIds { get; set; } = [];
    }

    public sealed partial class ConversionController
    {
        /// <summary>
        /// Queue conversions for a set of books chosen in the library.
        /// </summary>
        /// <remarks>
        /// Every book is reported on individually, and the response is 200 even when
        /// none were queued. A selection made from a list is expected to contain books
        /// that cannot convert - ones already M4B, ones already queued - so "some of
        /// these applied" is the normal outcome rather than an error, and no single
        /// status code can say which were which. The caller summarises
        /// <c>results</c>; it does not infer anything from the status.
        /// <para>
        /// Each book goes through the same path as the single-book request, including
        /// the manual trigger, so the automatic-conversion setting does not gate this
        /// and the per-book refusals are identical.
        /// </para>
        /// <para>
        /// Queued one at a time on purpose. Enqueueing writes a row guarded by a unique
        /// index, and a whole library fanned out at once would turn ordinary contention
        /// into rejected inserts that read as "already queued".
        /// </para>
        /// </remarks>
        /// <response code="200">Every requested book was considered; see the per-book results.</response>
        /// <response code="400">No ids were supplied.</response>
        [HttpPost("audiobooks/bulk")]
        public async Task<IActionResult> ConvertBulk(
            [FromBody] BulkConvertRequest request,
            CancellationToken cancellationToken = default)
        {
            // Distinct because a caller assembling ids from a grouped view can repeat
            // one, and queueing the same book twice only produces a self-inflicted
            // "already queued" in its own response.
            var audiobookIds = (request?.AudiobookIds ?? []).Distinct().ToList();
            if (audiobookIds.Count == 0)
            {
                return BadRequest(new { message = "No audiobooks were selected." });
            }

            var results = new List<object>(audiobookIds.Count);
            var queued = 0;

            foreach (var audiobookId in audiobookIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await conversionQueue.EnqueueAsync(
                    audiobookId,
                    ConversionTrigger.Manual,
                    cancellationToken);

                if (result.Queued)
                {
                    queued++;
                }

                results.Add(new
                {
                    audiobookId,
                    outcome = result.Outcome.ToString(),
                    jobId = result.JobId?.ToString(),
                    reason = result.Reason
                });
            }

            logger.LogInformation(
                "Bulk conversion request for {RequestedCount} audiobook(s): {QueuedCount} queued.",
                audiobookIds.Count,
                queued);

            return Ok(new
            {
                requestedCount = audiobookIds.Count,
                queuedCount = queued,
                results
            });
        }
    }
}
