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

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>Why a book was not queued for tagging. A refusal is reported, never swallowed.</summary>
    public enum TagEnqueueOutcome
    {
        Queued,

        /// <summary>An active job for this book already exists.</summary>
        AlreadyQueued,

        /// <summary>The book has no M4B files whose tags could be written.</summary>
        NothingToTag,

        /// <summary>No ffmpeg is installed, so queueing would only produce a failed job.</summary>
        WriterUnavailable,

        /// <summary>Automatic tagging is switched off and this was not a manual request.</summary>
        Disabled,

        /// <summary>The audiobook does not exist.</summary>
        NotFound
    }

    public sealed record TagEnqueueResult(
        TagEnqueueOutcome Outcome,
        Guid? JobId = null,
        string? Reason = null)
    {
        public bool Queued => Outcome == TagEnqueueOutcome.Queued;
    }

    /// <summary>
    /// The durable queue of tag-writing runs.
    ///
    /// Rewriting a book-sized container across a NAS share is slow, so it never runs on
    /// an import's critical path. Every entry point enqueues and returns; a worker does
    /// the work and reports progress.
    /// </summary>
    public interface ITagQueueService
    {
        /// <summary>
        /// Queue a tag write, or explain why one was not queued. A manual request ignores
        /// the automatic-tagging setting; everything else still applies.
        /// </summary>
        /// <remarks>
        /// <c>selectedTags</c> names the tags to write, or is null for every tag the
        /// mapping allows. <c>values</c> carries what the operator typed in the preview,
        /// replacing what those tags' patterns would produce. Both narrow this run only
        /// and neither touches the settings.
        /// </remarks>
        Task<TagEnqueueResult> EnqueueAsync(
            int audiobookId,
            TagTrigger trigger,
            IReadOnlyCollection<string>? selectedTags = null,
            IReadOnlyDictionary<string, string>? values = null,
            CancellationToken cancellationToken = default);

        /// <summary>Re-queue a terminal job that is allowed to retry.</summary>
        Task<TagEnqueueResult> RetryAsync(Guid jobId, CancellationToken cancellationToken = default);

        Task<TagJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);

        /// <summary>The active job for one book, if any. Drives the book page's button state.</summary>
        Task<TagJob?> GetActiveJobForAudiobookAsync(
            int audiobookId,
            CancellationToken cancellationToken = default);

        /// <summary>Every job worth showing in Activity: active, plus recent terminal ones.</summary>
        Task<IReadOnlyList<TagJob>> GetVisibleJobsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Take the next job whose lease is free and whose retry time has arrived, marking
        /// it Running under <paramref name="leaseOwner"/>. Returns null when there is none.
        /// </summary>
        Task<TagJob?> ClaimNextAsync(string leaseOwner, CancellationToken cancellationToken = default);

        /// <summary>
        /// Extend the lease of a job still being worked. Returns false when the lease has
        /// been lost, which means another worker owns the job and this one must stop.
        /// </summary>
        Task<bool> HeartbeatAsync(Guid jobId, string leaseOwner, CancellationToken cancellationToken = default);

        Task ReportProgressAsync(
            Guid jobId,
            TagJobPhase phase,
            double progress,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Remember that this job holds a verified rewrite it has not published, and that
        /// the library file it replaces has already been removed.
        ///
        /// Written before the original is removed, not after: a crash between the two
        /// must leave a row pointing at the replacement, never a book whose only copy has
        /// gone with nothing recording where the other one is.
        /// </summary>
        Task RecordPendingPublicationAsync(
            Guid jobId,
            int fileId,
            string outputPath,
            long outputLength,
            string destinationPath,
            CancellationToken cancellationToken = default);

        /// <summary>Forget a pending publication, because it has been published.</summary>
        Task ClearPendingPublicationAsync(Guid jobId, CancellationToken cancellationToken = default);

        Task CompleteAsync(Guid jobId, int tagsWritten, CancellationToken cancellationToken = default);

        /// <summary>
        /// Record a failed attempt. Schedules another attempt when one is left and the
        /// failure is worth retrying; otherwise the job becomes terminally failed.
        /// </summary>
        Task FailAsync(
            Guid jobId,
            TagWriteFailureKind failureKind,
            string error,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Return jobs abandoned by a previous process to the queue. Called at startup,
        /// because a job left Running by a restart owns a lease nobody will renew.
        /// </summary>
        Task RecoverAbandonedJobsAsync(CancellationToken cancellationToken = default);
    }
}
