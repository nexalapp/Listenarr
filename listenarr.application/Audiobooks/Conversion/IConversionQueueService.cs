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

namespace Listenarr.Application.Audiobooks.Conversion
{
    /// <summary>Why a book was not queued. A refusal is reported, never swallowed.</summary>
    public enum ConversionEnqueueOutcome
    {
        Queued,

        /// <summary>An active job for this book already exists.</summary>
        AlreadyQueued,

        /// <summary>The book has no files that would benefit from converting.</summary>
        NothingToConvert,

        /// <summary>No encoder is installed, so queueing would only produce a failed job.</summary>
        EncoderUnavailable,

        /// <summary>Automatic conversion is switched off and this was not a manual request.</summary>
        Disabled,

        /// <summary>The audiobook does not exist.</summary>
        NotFound
    }

    public sealed record ConversionEnqueueResult(
        ConversionEnqueueOutcome Outcome,
        Guid? JobId = null,
        string? Reason = null)
    {
        public bool Queued => Outcome == ConversionEnqueueOutcome.Queued;
    }

    /// <summary>
    /// The durable queue of audiobook conversions.
    ///
    /// Conversion is slow and IO-heavy against a NAS share, so it never runs on an
    /// import's critical path. Both entry points enqueue and return; a worker does
    /// the work and reports progress.
    /// </summary>
    public interface IConversionQueueService
    {
        /// <summary>
        /// Queue a conversion, or explain why one was not queued. A manual request
        /// ignores the automatic-conversion setting; everything else still applies.
        /// </summary>
        Task<ConversionEnqueueResult> EnqueueAsync(
            int audiobookId,
            ConversionTrigger trigger,
            CancellationToken cancellationToken = default);

        /// <summary>Re-queue a terminal job that is allowed to retry.</summary>
        Task<ConversionEnqueueResult> RetryAsync(
            Guid jobId,
            CancellationToken cancellationToken = default);

        Task<ConversionJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);

        /// <summary>The active job for one book, if any. Drives the book page's button state.</summary>
        Task<ConversionJob?> GetActiveJobForAudiobookAsync(
            int audiobookId,
            CancellationToken cancellationToken = default);

        /// <summary>Every job worth showing in Activity: active, plus recent terminal ones.</summary>
        Task<IReadOnlyList<ConversionJob>> GetVisibleJobsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Take the next job whose lease is free and whose retry time has arrived, marking
        /// it Running under <paramref name="leaseOwner"/>. Returns null when there is none.
        /// </summary>
        Task<ConversionJob?> ClaimNextAsync(
            string leaseOwner,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Extend the lease of a job still being worked. Returns false when the lease has
        /// been lost, which means another worker owns the job and this one must stop.
        /// </summary>
        Task<bool> HeartbeatAsync(
            Guid jobId,
            string leaseOwner,
            CancellationToken cancellationToken = default);

        Task ReportProgressAsync(
            Guid jobId,
            ConversionJobPhase phase,
            double progress,
            CancellationToken cancellationToken = default);

        Task CompleteAsync(
            Guid jobId,
            string outputPath,
            int chapterCount,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Record a failed attempt. Schedules another attempt when one is left and the
        /// failure is worth retrying; otherwise the job becomes terminally failed.
        /// </summary>
        Task FailAsync(
            Guid jobId,
            ConversionFailureKind failureKind,
            string error,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Return jobs abandoned by a previous process to the queue. Called at startup,
        /// because a job left Running by a restart owns a lease nobody will renew.
        /// </summary>
        Task RecoverAbandonedJobsAsync(CancellationToken cancellationToken = default);
    }
}
