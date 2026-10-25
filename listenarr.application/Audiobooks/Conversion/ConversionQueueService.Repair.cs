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
using Listenarr.Application.Audiobooks.Repair;
using Listenarr.Domain.Audiobooks.Conversion;
using Listenarr.Domain.Audiobooks.Repair;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Conversion
{
    /// <summary>
    /// Repairs on the conversion queue: the same table, the same one-active-job-per-book
    /// key — a book being repaired must not also be converting, and the shared key makes
    /// that a database guarantee — but its own refusals, because a repair has one more
    /// thing to be missing.
    /// </summary>
    public sealed partial class ConversionQueueService
    {
        public async Task<ConversionEnqueueResult> EnqueueRepairAsync(
            int audiobookId,
            CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.NotFound,
                    Reason: "That audiobook no longer exists.");
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            if (!settings.RepairEnabled)
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.Disabled,
                    Reason: "Audio repair is switched off. Turn it on in Settings → Conversion & Tags.");
            }

            // Repairable is broader than convertible: a damaged book already merged into
            // one M4B is exactly as repairable as the MP3s it was merged from.
            var repairable = (audiobook.Files ?? []).Count(file => RepairableSource.IsRepairable(file.Path));
            if (repairable == 0)
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.NothingToConvert,
                    Reason: "This book has no audio files to repair.");
            }

            var unreadable = DescribeUnreadableFiles(audiobook);
            if (unreadable != null)
            {
                return new ConversionEnqueueResult(ConversionEnqueueOutcome.SourceUnreadable, Reason: unreadable);
            }

            if (!await converter.IsAvailableAsync(cancellationToken))
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.EncoderUnavailable,
                    Reason: "No ffmpeg encoder is installed, so repair is unavailable.");
            }

            // The enhancer is checked at enqueue like the encoder is: a row that can only
            // fail the moment a worker claims it tells the operator nothing the refusal
            // does not say better, and sooner.
            if (enhancer == null || !await enhancer.IsAvailableAsync(cancellationToken))
            {
                var status = enhancer == null ? null : await enhancer.GetStatusAsync(cancellationToken);
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.EnhancerUnavailable,
                    Reason: status?.State switch
                    {
                        EnhancerState.Provisioning =>
                            $"The enhancement model is still downloading ({status.ProvisionProgress:0}%). Repair becomes available when it lands.",
                        EnhancerState.Failed =>
                            $"The enhancement model could not be provisioned: {status.Error}",
                        _ => "The enhancement model is not provisioned. Enabling repair in Settings downloads it (about 4 GB, once)."
                    });
            }

            var existing = await repository.GetActiveForAudiobookAsync(audiobookId, cancellationToken);
            if (existing != null)
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.AlreadyQueued,
                    existing.Id,
                    existing.Kind == ConversionJobKind.Repair
                        ? "This book is already queued for repair."
                        : "This book is queued for conversion; a repair would rewrite the same files.");
            }

            var job = new ConversionJob
            {
                AudiobookId = audiobookId,
                Kind = ConversionJobKind.Repair,
                // A repair is only ever asked for by a person, per book, after an A/B
                // listen. The trigger says so, and nothing automatic sets it otherwise.
                Trigger = ConversionTrigger.Manual,
                SourceFileCount = repairable,
                ActiveDeduplicationKey = ConversionJob.BuildDeduplicationKey(audiobookId),
                EnqueuedAt = timeProvider.GetUtcNow().UtcDateTime
            };

            var stored = await repository.AddAsync(job, cancellationToken);
            if (stored == null)
            {
                return new ConversionEnqueueResult(
                    ConversionEnqueueOutcome.AlreadyQueued,
                    Reason: "This book already has a conversion or repair queued.");
            }

            logger.LogInformation(
                "Queued repair {JobId} for audiobook {AudiobookId} ({FileCount} file(s))",
                stored.Id,
                audiobookId,
                repairable);

            await BroadcastAsync(stored, cancellationToken);
            return new ConversionEnqueueResult(ConversionEnqueueOutcome.Queued, stored.Id);
        }
    }
}
