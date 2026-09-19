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
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// Chapter repairs and audio audits on the tag queue. The same table, the same one-active-job-per-book
    /// rule and the same worker as a tag write, because both replace the book's files
    /// through the same publication path; only what gets written differs.
    /// </summary>
    public sealed partial class TagQueueService
    {
        public async Task<TagEnqueueResult> EnqueueChapterRepairAsync(
            int audiobookId,
            IReadOnlyDictionary<int, ChapterPlan> plans,
            TagTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plans);

            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.NotFound, Reason: "That audiobook no longer exists.");
            }

            var known = new HashSet<int>((audiobook.Files ?? []).Where(file => TaggableFile.IsTaggable(file.Path)).Select(file => file.Id));
            var accepted = plans.Where(pair => known.Contains(pair.Key) && pair.Value.Chapters.Count > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (accepted.Count == 0)
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.NothingToTag,
                    Reason: "None of the selected files is an M4B of this book with a chapter list to write.");
            }

            if (!await tagWriter.IsAvailableAsync(cancellationToken))
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.WriterUnavailable,
                    Reason: "No ffmpeg is installed, so chapters cannot be rewritten.");
            }

            var existing = await repository.GetActiveForAudiobookAsync(audiobookId, cancellationToken);
            if (existing != null)
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.AlreadyQueued,
                    existing.Id,
                    "This book already has a write queued.");
            }

            var job = new TagJob
            {
                AudiobookId = audiobookId,
                Trigger = trigger,
                Kind = TagJobKind.Chapters,
                FileCount = accepted.Count,
                SelectedFileIdsJson = SerializeFileIds(accepted.Keys.ToList()),
                ChapterPlanJson = SerializeChapterPlans(accepted),
                ActiveDeduplicationKey = TagJob.BuildDeduplicationKey(audiobookId),
                EnqueuedAt = timeProvider.GetUtcNow().UtcDateTime
            };

            var stored = await repository.AddAsync(job, cancellationToken);
            if (stored == null)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.AlreadyQueued, Reason: "This book already has a write queued.");
            }

            logger.LogInformation(
                "Queued chapter repair {JobId} for audiobook {AudiobookId} ({FileCount} file(s))",
                stored.Id,
                audiobookId,
                accepted.Count);

            await BroadcastAsync(stored, cancellationToken);
            return new TagEnqueueResult(TagEnqueueOutcome.Queued, stored.Id);
        }
        public async Task<TagEnqueueResult> EnqueueAudioAuditAsync(
            int audiobookId,
            TagTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.NotFound, Reason: "That audiobook no longer exists.");
            }

            var audioFiles = (audiobook.Files ?? []).Count(file => FileUtils.IsAudioFile(file.Path ?? string.Empty));
            if (audioFiles == 0)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.NothingToTag, Reason: "This book has no audio files to listen to.");
            }

            var existing = await repository.GetActiveForAudiobookAsync(audiobookId, TagJobKind.Audit, cancellationToken);
            if (existing != null)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.AlreadyQueued, existing.Id, "This book is already queued for an audio audit.");
            }

            var job = new TagJob
            {
                AudiobookId = audiobookId,
                Trigger = trigger,
                Kind = TagJobKind.Audit,
                FileCount = Math.Min(audioFiles, 2),
                ActiveDeduplicationKey = TagJob.BuildAuditDeduplicationKey(audiobookId),
                EnqueuedAt = timeProvider.GetUtcNow().UtcDateTime
            };

            var stored = await repository.AddAsync(job, cancellationToken);
            if (stored == null)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.AlreadyQueued, Reason: "This book is already queued for an audio audit.");
            }

            logger.LogInformation("Queued audio audit {JobId} for audiobook {AudiobookId}", stored.Id, audiobookId);
            await BroadcastAsync(stored, cancellationToken);
            return new TagEnqueueResult(TagEnqueueOutcome.Queued, stored.Id);
        }
        public async Task<TagEnqueueResult> EnqueueChapterPlanAsync(
            int audiobookId,
            IReadOnlyCollection<int> fileIds,
            TagTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fileIds);
            if (fileIds.Count == 0)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.NothingToTag, Reason: "No files to plan for.");
            }

            var existing = await repository.GetActiveForAudiobookAsync(audiobookId, TagJobKind.Plan, cancellationToken);
            if (existing != null)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.AlreadyQueued, existing.Id, "This book's chapter fix is already being worked out.");
            }

            var job = new TagJob
            {
                AudiobookId = audiobookId,
                Trigger = trigger,
                Kind = TagJobKind.Plan,
                FileCount = fileIds.Count,
                SelectedFileIdsJson = SerializeFileIds(fileIds),
                ActiveDeduplicationKey = TagJob.BuildPlanDeduplicationKey(audiobookId),
                EnqueuedAt = timeProvider.GetUtcNow().UtcDateTime
            };

            var stored = await repository.AddAsync(job, cancellationToken);
            if (stored == null)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.AlreadyQueued, Reason: "This book's chapter fix is already being worked out.");
            }

            logger.LogInformation("Queued chapter planning {JobId} for audiobook {AudiobookId} ({FileCount} file(s))", stored.Id, audiobookId, fileIds.Count);
            await BroadcastAsync(stored, cancellationToken);
            return new TagEnqueueResult(TagEnqueueOutcome.Queued, stored.Id);
        }
    }
}
