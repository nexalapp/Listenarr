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
using System.ComponentModel.DataAnnotations;

namespace Listenarr.Domain.Audiobooks.Tagging
{
    public enum TagJobStatus
    {
        Queued,
        Running,
        RetryScheduled,
        Completed,
        Failed,
        Cancelled,
        Superseded
    }

    /// <summary>Where a running job has got to. Named for the Activity view, not for a log.</summary>
    public enum TagJobPhase
    {
        None,
        Reading,
        Writing,
        Verifying,
        Publishing
    }

    /// <summary>How the job came to exist. A manual request ignores the automatic setting.</summary>
    public enum TagJobKind
    {
        Tags,
        Chapters,

        /// <summary>Listen to the book and record whether it is what its record says. Writes nothing to the file.</summary>
        Audit
    }

    public enum TagTrigger
    {
        Automatic,
        Manual
    }

    public static class TagJobStatusExtensions
    {
        public static bool IsActive(this TagJobStatus status) => status is
            TagJobStatus.Queued or
            TagJobStatus.Running or
            TagJobStatus.RetryScheduled;

        public static bool IsTerminal(this TagJobStatus status) => !status.IsActive();
    }

    /// <summary>
    /// One durable request to write metadata into an audiobook's M4B files.
    ///
    /// The row outlives the process for the same reason a conversion's does: rewriting a
    /// 600MB container across a NAS share is not instant, and a restart mid-write has to
    /// leave something a worker can pick up rather than a book whose file has been
    /// replaced by nothing.
    /// </summary>
    public class TagJob
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public int AudiobookId { get; set; }

        public TagJobStatus Status { get; set; } = TagJobStatus.Queued;
        public TagJobPhase Phase { get; set; } = TagJobPhase.None;
        public TagTrigger Trigger { get; set; } = TagTrigger.Automatic;

        /// <summary>
        /// Whether this job writes tags or rewrites chapters. One queue for both because
        /// they replace the same files through the same publication path, and one active
        /// job per book is exactly the guarantee that path needs.
        /// </summary>
        public TagJobKind Kind { get; set; } = TagJobKind.Tags;

        /// <summary>
        /// For a chapter job: the chapter list to write for each file, as JSON keyed by
        /// file id. Decided at enqueue time so what the operator previewed is what gets
        /// written, whatever the file reads as by the time the worker gets to it.
        /// </summary>
        public string? ChapterPlanJson { get; set; }

        /// <summary>
        /// Set while the job is active and cleared when it reaches a terminal state. A
        /// unique index over this column is what stops an import and an operator from
        /// queueing two rewrites of the same book's files at once.
        /// </summary>
        [MaxLength(256)]
        public string? ActiveDeduplicationKey { get; set; }

        public double Progress { get; set; }

        /// <summary>How many of the book's files this job will rewrite.</summary>
        public int FileCount { get; set; }

        /// <summary>How many tags were actually written, summed over the book's files.</summary>
        public int TagsWritten { get; set; }

        /// <summary>
        /// The tags the operator chose for this run, as a JSON array of tag keys.
        ///
        /// Null means "every tag the mapping allows", which is what an automatic run
        /// does. A preview lets the operator narrow it, and that choice belongs to the
        /// run rather than to the settings: unticking a field once should not silently
        /// change what every later book gets.
        /// </summary>
        public string? SelectedTagsJson { get; set; }

        /// <summary>
        /// Which of the book's files this run should write, or null for all of them.
        /// </summary>
        /// <remarks>
        /// A book is not always one file, and its files are not always the same work: a
        /// collection of short stories arrives as one Audible title whose parts carry
        /// different names. Without this the job could only be told which book to write,
        /// so a table that let one part be ticked would have been promising something the
        /// job could not do.
        /// <para>
        /// Null rather than every id, so a job queued before this existed — or by the
        /// automatic run, which is about the whole book — still means what it meant.
        /// </para>
        /// </remarks>
        public string? SelectedFileIdsJson { get; set; }

        /// <summary>
        /// Values the operator typed in the preview, as a JSON object of tag to value.
        ///
        /// Null means every value comes from its pattern, which is what an automatic run
        /// does. A preview is where a provider's mistake becomes visible — a wrong series
        /// position, a title with the wrong case — and correcting it there has to survive
        /// until the worker picks the job up.
        /// </summary>
        public string? OverriddenValuesJson { get; set; }

        /// <summary>
        /// A verified rewrite this job produced but has not yet published, and the
        /// library path it belongs at.
        ///
        /// <para>
        /// Set only after the rewritten file has been read back and its tags, chapters,
        /// cover art and duration confirmed — and, critically, only while the original at
        /// <see cref="PendingDestinationPath"/> has already been removed to make room for
        /// it. Between those two moments the library has no file for this book, and this
        /// row is the only thing that knows where the replacement is. Nothing may delete
        /// the file it names until the publication succeeds.
        /// </para>
        /// <para>
        /// The rewrite is a stream copy, so it carries the original's audio bit for bit.
        /// That is what makes removing the original first an acceptable trade rather than
        /// a destructive one: the replacement is a strict superset of what was removed.
        /// </para>
        /// </summary>
        [MaxLength(2000)]
        public string? PendingOutputPath { get; set; }

        /// <summary>
        /// Byte length of <see cref="PendingOutputPath"/> as verified. A file whose size
        /// no longer matches is not the one that was checked.
        /// </summary>
        public long? PendingOutputLength { get; set; }

        /// <summary>Where <see cref="PendingOutputPath"/> belongs in the library.</summary>
        [MaxLength(2000)]
        public string? PendingDestinationPath { get; set; }

        /// <summary>Which registered file the pending publication replaces.</summary>
        public int? PendingFileId { get; set; }

        /// <summary>
        /// Operator-facing reason for a terminal failure, written to be read in the
        /// Activity view.
        /// </summary>
        public string? Error { get; set; }

        [MaxLength(32)]
        public string? FailureKind { get; set; }

        public bool CanRetry { get; set; } = true;

        public int AttemptCount { get; set; }
        public int MaxAttempts { get; set; } = 3;
        public DateTime? NextAttemptAt { get; set; }

        [MaxLength(200)]
        public string? LeaseOwner { get; set; }
        public DateTime? LeaseExpiresAt { get; set; }
        public int LeaseGeneration { get; set; }

        public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>One tagging run per audiobook: a second request while one is in flight is the same request.</summary>
        public static string BuildDeduplicationKey(int audiobookId) => $"tagging:{audiobookId}";

        /// <summary>
        /// An audit's key is its own: it writes nothing, so it may sit in the queue
        /// beside a tag write for the same book without either being refused.
        /// </summary>
        public static string BuildAuditDeduplicationKey(int audiobookId) => $"audit:{audiobookId}";
    }
}
