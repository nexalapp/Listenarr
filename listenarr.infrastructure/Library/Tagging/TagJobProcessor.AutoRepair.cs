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
using Listenarr.Application.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Quality;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Tagging
{
    /// <summary>
    /// Auto-repair: the moment an import's own probe or chapter planning finds a need, the
    /// repair is queued without anyone asking — when its setting is on. Keyed on the
    /// <see cref="TagTrigger.Import"/> trigger, so a need found in the existing library
    /// (the probe sweep, a table load, Check Chapters) is shown, never acted on.
    ///
    /// <para>
    /// Both are post-completion effects: the probe or plan has already been stored, so a
    /// refusal or a failure here is logged and must not fail the job that found the need.
    /// </para>
    /// </summary>
    public sealed partial class TagJobProcessor
    {
        private async Task OfferAutoAudioRepairAsync(
            TagJob job,
            Audiobook audiobook,
            AudioQualityReport report,
            IServiceProvider services,
            CancellationToken cancellationToken)
        {
            if (job.Trigger != TagTrigger.Import || !report.GenerativeRecommended)
            {
                return;
            }

            try
            {
                var settings = await services.GetRequiredService<IConfigurationService>().GetApplicationSettingsAsync();
                if (!settings.AutoRepairAudio)
                {
                    return;
                }

                var result = await services.GetRequiredService<IConversionQueueService>()
                    .EnqueueRepairAsync(audiobook.Id, cancellationToken);
                logger.LogInformation(
                    "Auto-repair of audio for imported audiobook {AudiobookId}: {Outcome} {Reason}",
                    audiobook.Id,
                    result.Outcome,
                    result.Reason);
            }
            catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
            {
                logger.LogWarning(exception, "Could not queue the auto-repair of audiobook {AudiobookId}'s audio", audiobook.Id);
            }
        }

        private async Task OfferAutoChapterRepairAsync(
            TagJob job,
            Audiobook audiobook,
            IReadOnlyCollection<int>? fileIds,
            int filesWithFix,
            IServiceProvider services,
            CancellationToken cancellationToken)
        {
            if (job.Trigger != TagTrigger.Import || filesWithFix == 0)
            {
                return;
            }

            try
            {
                var settings = await services.GetRequiredService<IConfigurationService>().GetApplicationSettingsAsync();
                if (!settings.AutoRepairChapters)
                {
                    return;
                }

                var result = await services.GetRequiredService<IChapterRepairService>()
                    .EnqueueAsync(audiobook.Id, fileIds, cancellationToken);
                logger.LogInformation(
                    "Auto-repair of chapters for imported audiobook {AudiobookId}: {Outcome} {Reason}",
                    audiobook.Id,
                    result.Outcome,
                    result.Reason);
            }
            catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
            {
                logger.LogWarning(exception, "Could not queue the auto-repair of audiobook {AudiobookId}'s chapters", audiobook.Id);
            }
        }
    }
}
