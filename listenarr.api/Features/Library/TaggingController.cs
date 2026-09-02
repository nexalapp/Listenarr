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
using Listenarr.Application.Audiobooks;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    /// <summary>
    /// What one tagging run should write.
    /// </summary>
    /// <remarks>
    /// <c>Tags</c> names the tags to write, or is null for every tag the mapping allows.
    /// <c>Values</c> carries what the operator typed in the preview, replacing what
    /// those tags' patterns would produce — a provider's wrong series position is
    /// correctable for one book without editing the mapping every book shares.
    /// </remarks>
    public sealed class WriteTagsRequest
    {
        public List<string>? Tags { get; set; }

        public Dictionary<string, string>? Values { get; set; }
    }

    /// <summary>
    /// Writing Audible metadata into a book's M4B files.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/tagging")]
    [Tags("Library")]
    public sealed class TaggingController(
        ITagQueueService tagQueue,
        ITagPreviewService previewService,
        ILibraryTagIndexService tagIndex,
        IConfigurationService configurationService,
        ILogger<TaggingController> logger) : ControllerBase
    {
        /// <summary>
        /// Every audio file in the library with the tags it actually carries.
        /// </summary>
        /// <remarks>
        /// One row per file rather than per book: a book split into parts can have one
        /// part tagged differently from the rest, and a per-book table is exactly the
        /// shape that hides it.
        /// <para>
        /// The whole library comes back in one response, uncapped, because sorting a tag
        /// table by a tag means sorting by a value that only exists once every file has
        /// been read — paging it server-side would buy nothing and cost the client the
        /// ability to re-sort without a round trip. Reads are cached per file against its
        /// size and modification time, so only a cold load touches the disk.
        /// </para>
        /// </remarks>
        /// <param name="refresh">Re-probe every file instead of trusting the cache.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("library")]
        public async Task<IActionResult> GetLibraryTags(
            [FromQuery] bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            var index = await tagIndex.BuildAsync(refresh, cancellationToken);

            return Ok(new
            {
                generatedAt = index.GeneratedAtUtc,
                filesRead = index.FilesRead,
                // The catalog travels with the rows so the table can build its columns
                // from the same list the settings screen and the writer use, rather than
                // from whichever tags happened to appear in the data.
                columns = TagCatalog.Definitions.Select(definition => new
                {
                    tag = definition.Tag,
                    label = definition.Label,
                    isLongText = definition.IsLongText
                }),
                rows = index.Rows.Select(row => new
                {
                    audiobookId = row.AudiobookId,
                    fileId = row.FileId,
                    bookTitle = row.BookTitle,
                    fileName = row.FileName,
                    path = row.RelativePath,
                    extension = row.Extension,
                    writable = row.Writable,
                    tags = row.Tags,
                    expected = row.Expected,
                    mismatched = row.Mismatched,
                    error = row.Error
                })
            });
        }

        /// <summary>
        /// The tags Listenarr can write, with their current mapping.
        /// </summary>
        /// <remarks>
        /// The catalog and the saved mapping are returned together because they are only
        /// meaningful together: the settings screen needs each tag's default and
        /// description alongside whatever the operator has changed it to.
        /// </remarks>
        [HttpGet("tags")]
        public async Task<IActionResult> GetTags(CancellationToken cancellationToken = default)
        {
            var settings = await configurationService.GetApplicationSettingsAsync();
            var mappings = TagCatalog.Reconcile(settings.TagMappings)
                .ToDictionary(mapping => mapping.Tag, StringComparer.OrdinalIgnoreCase);

            return Ok(TagCatalog.Definitions.Select(definition => new
            {
                tag = definition.Tag,
                label = definition.Label,
                description = definition.Description,
                defaultPattern = definition.DefaultPattern,
                defaultMode = definition.DefaultMode.ToString(),
                isLongText = definition.IsLongText,
                pattern = mappings.TryGetValue(definition.Tag, out var mapping)
                    ? mapping.Pattern
                    : definition.DefaultPattern,
                mode = (mappings.TryGetValue(definition.Tag, out var current)
                    ? current.Mode
                    : definition.DefaultMode).ToString()
            }));
        }

        /// <summary>
        /// What writing tags to this book would change, before anything is written.
        /// </summary>
        /// <param name="audiobookId">The book to preview.</param>
        /// <param name="tags">
        /// Restrict the preview to these tags. Omit for every tag the mapping allows.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [HttpGet("audiobooks/{audiobookId:int}/preview")]
        public async Task<IActionResult> Preview(
            int audiobookId,
            [FromQuery] string[]? tags = null,
            CancellationToken cancellationToken = default)
        {
            var preview = await previewService.BuildAsync(
                audiobookId,
                tags is { Length: > 0 } ? tags : null,
                cancellationToken);

            return Ok(new
            {
                audiobookId = preview.AudiobookId,
                title = preview.Title,
                canWrite = preview.CanWrite,
                hasChanges = preview.HasChanges,
                reason = preview.Reason,
                files = preview.Files.Select(file => new
                {
                    fileId = file.FileId,
                    name = file.Name,
                    error = file.Error,
                    changes = file.Changes.Select(change => new
                    {
                        tag = change.Tag,
                        label = change.Label,
                        current = change.Current,
                        proposed = change.Proposed,
                        action = change.Action.ToString(),
                        reason = change.Reason,
                        // A blurb typed into a single-line box cannot be read, let alone
                        // corrected, so the preview needs to know which is which.
                        isLongText = change.IsLongText
                    })
                })
            });
        }

        /// <summary>
        /// Queue a tag write for one book.
        /// </summary>
        /// <remarks>
        /// A manual request runs regardless of the automatic-tagging setting; the other
        /// refusals (no M4B files, no ffmpeg, already queued) still apply and are returned
        /// with the reason rather than as a generic failure.
        /// </remarks>
        /// <response code="202">The tag write was queued.</response>
        /// <response code="404">No such audiobook.</response>
        /// <response code="409">Already queued, or there is nothing to tag.</response>
        /// <response code="503">No ffmpeg is installed.</response>
        [HttpPost("audiobooks/{audiobookId:int}")]
        public async Task<IActionResult> Write(
            int audiobookId,
            [FromBody] WriteTagsRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            var result = await tagQueue.EnqueueAsync(
                audiobookId,
                TagTrigger.Manual,
                request?.Tags is { Count: > 0 } selected ? selected : null,
                request?.Values is { Count: > 0 } values ? values : null,
                cancellationToken);

            logger.LogInformation(
                "Manual tag write request for audiobook {AudiobookId}: {Outcome}",
                audiobookId,
                result.Outcome);

            return ToResponse(result);
        }

        /// <summary>
        /// Re-run a tag write that failed.
        /// </summary>
        /// <remarks>
        /// A failed job may be holding the only copy of a library file it had already
        /// removed. Retrying is what puts that file back, so this is not merely a
        /// convenience.
        /// </remarks>
        /// <response code="202">The tag write was queued again.</response>
        /// <response code="404">No such tag-writing job.</response>
        /// <response code="409">That tag write is already running.</response>
        [HttpPost("jobs/{jobId:guid}/retry")]
        public async Task<IActionResult> Retry(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var result = await tagQueue.RetryAsync(jobId, cancellationToken);
            return ToResponse(result);
        }

        /// <summary>
        /// Stop a tag write that has not finished.
        /// </summary>
        /// <response code="200">The tag write was cancelled.</response>
        /// <response code="404">No such job.</response>
        /// <response code="409">It had already finished, or it cannot be abandoned safely.</response>
        [HttpPost("jobs/{jobId:guid}/cancel")]
        public async Task<IActionResult> Cancel(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var result = await tagQueue.CancelAsync(jobId, cancellationToken);
            return ToControlResponse(result);
        }

        /// <summary>
        /// Clear a finished tag write out of Activity.
        /// </summary>
        /// <response code="200">The job was removed.</response>
        /// <response code="404">No such job.</response>
        /// <response code="409">It is still running, or it cannot be removed safely.</response>
        [HttpDelete("jobs/{jobId:guid}")]
        public async Task<IActionResult> Dismiss(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var result = await tagQueue.DismissAsync(jobId, cancellationToken);
            return ToControlResponse(result);
        }

        /// <summary>
        /// A refusal carries the reason the operator needs, because they are looking at
        /// the row they asked about and it is still there.
        /// </summary>
        private IActionResult ToControlResponse(JobControlResult result) => result.Outcome switch
        {
            JobControlOutcome.Done => Ok(new { ok = true }),
            JobControlOutcome.NotFound => NotFound(new { ok = false, reason = result.Reason }),
            _ => Conflict(new { ok = false, reason = result.Reason })
        };

        /// <summary>Every tag write worth showing: active jobs, plus recently finished ones.</summary>
        [HttpGet("jobs")]
        public async Task<IActionResult> GetJobs(CancellationToken cancellationToken = default)
        {
            var jobs = await tagQueue.GetVisibleJobsAsync(cancellationToken);
            return Ok(jobs.Select(Project));
        }

        /// <summary>
        /// The active tag write for one book, if there is one. Drives the book page's
        /// button state.
        /// </summary>
        /// <response code="204">No tag write is active for this book.</response>
        [HttpGet("audiobooks/{audiobookId:int}")]
        public async Task<IActionResult> GetForAudiobook(
            int audiobookId,
            CancellationToken cancellationToken = default)
        {
            var job = await tagQueue.GetActiveJobForAudiobookAsync(audiobookId, cancellationToken);
            return job == null ? NoContent() : Ok(Project(job));
        }

        private IActionResult ToResponse(TagEnqueueResult result) => result.Outcome switch
        {
            TagEnqueueOutcome.Queued =>
                Accepted(new { jobId = result.JobId, queued = true }),

            TagEnqueueOutcome.NotFound =>
                NotFound(new { queued = false, reason = result.Reason }),

            TagEnqueueOutcome.WriterUnavailable =>
                StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new { queued = false, reason = result.Reason }),

            // Already queued and nothing-to-tag are both "the request cannot apply to
            // this book right now", which is what 409 says.
            _ => Conflict(new { queued = false, jobId = result.JobId, reason = result.Reason })
        };

        /// <summary>
        /// The public shape of a job. Deliberately not the entity: lease ownership, the
        /// deduplication key and the held scratch path are internal scheduling state.
        /// </summary>
        private static object Project(TagJob job) => new
        {
            jobId = job.Id.ToString(),
            audiobookId = job.AudiobookId,
            status = job.Status.ToString(),
            phase = job.Phase.ToString(),
            progress = job.Progress,
            trigger = job.Trigger.ToString(),
            fileCount = job.FileCount,
            tagsWritten = job.TagsWritten,
            error = job.Error,
            failureKind = job.FailureKind,
            canRetry = job.CanRetry,
            // A job in this state has removed a library file and is holding its only
            // replacement. The UI needs to say so rather than showing an ordinary failure.
            holdingUnpublishedFile = !string.IsNullOrWhiteSpace(job.PendingOutputPath),
            attemptCount = job.AttemptCount,
            enqueuedAt = job.EnqueuedAt,
            completedAt = job.CompletedAt
        };
    }
}
