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

namespace Listenarr.Domain.Audiobooks.Conversion
{
    public enum ConversionJobStatus
    {
        Queued,
        Running,
        RetryScheduled,
        Completed,
        Failed,
        Cancelled,
        Superseded
    }

    /// <summary>
    /// Where a running job has got to. Reported to the operator, so the names are the
    /// ones the Activity view shows rather than internal stage numbers.
    /// </summary>
    public enum ConversionJobPhase
    {
        None,
        Probing,
        Encoding,
        Verifying,
        Publishing,
        RetiringSources
    }

    /// <summary>How the job came to exist. A manual request is never skipped by the setting.</summary>
    public enum ConversionTrigger
    {
        Automatic,
        Manual
    }

    public static class ConversionJobStatusExtensions
    {
        public static bool IsActive(this ConversionJobStatus status) => status is
            ConversionJobStatus.Queued or
            ConversionJobStatus.Running or
            ConversionJobStatus.RetryScheduled;

        public static bool IsTerminal(this ConversionJobStatus status) => !status.IsActive();
    }

    /// <summary>
    /// One durable request to convert an audiobook's files into a single M4B.
    ///
    /// The row outlives the process: a conversion can run for an hour against a NAS
    /// share, and a restart mid-encode has to leave something a worker can pick up
    /// rather than a book stuck half-converted with nothing recording that it was.
    /// </summary>
    public class ConversionJob
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public int AudiobookId { get; set; }

        public ConversionJobStatus Status { get; set; } = ConversionJobStatus.Queued;
        public ConversionJobPhase Phase { get; set; } = ConversionJobPhase.None;
        public ConversionTrigger Trigger { get; set; } = ConversionTrigger.Automatic;

        /// <summary>
        /// Set while the job is active and cleared when it reaches a terminal state. A
        /// unique index over this column is what stops an import and an operator from
        /// queueing two conversions of the same book at once.
        /// </summary>
        [MaxLength(256)]
        public string? ActiveDeduplicationKey { get; set; }

        /// <summary>Fraction complete, 0 to 100. Persisted so a reconnecting client sees real progress.</summary>
        public double Progress { get; set; }

        public int SourceFileCount { get; set; }
        public int ChapterCount { get; set; }

        /// <summary>Where the finished M4B was published, once it has been.</summary>
        [MaxLength(2000)]
        public string? OutputPath { get; set; }

        /// <summary>
        /// Operator-facing reason for a terminal failure. Written to be read in the
        /// Activity view, so it names the file or the limit rather than pointing at a log.
        /// </summary>
        public string? Error { get; set; }

        [MaxLength(32)]
        public string? FailureKind { get; set; }

        /// <summary>Whether a failed job is worth offering a retry button for.</summary>
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

        /// <summary>
        /// Deduplication key for an active job. One conversion per audiobook: a second
        /// request while one is in flight is the same request.
        /// </summary>
        public static string BuildDeduplicationKey(int audiobookId) =>
            $"conversion:{audiobookId}";
    }
}
