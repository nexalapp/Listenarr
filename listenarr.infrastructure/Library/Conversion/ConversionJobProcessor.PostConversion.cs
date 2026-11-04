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
using Listenarr.Application.Audiobooks.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Conversion
{
    /// <summary>
    /// The import checks a conversion stood in front of. The scan hook hands a book with
    /// convertible sources to conversion and stops there, because the file it would
    /// check does not exist yet — so without this, every download that arrived as parts
    /// skipped its quality probe, chapter verdict and audit, and a codec-damaged one was
    /// never offered for repair. Red Specter came in as 47 MP3s at 34 kbps, was converted,
    /// and was found by the library sweep a day later, which shows a need and never acts.
    /// </summary>
    public sealed partial class ConversionJobProcessor
    {
        /// <summary>
        /// Run them now, with the import's trigger. Only for an automatic conversion: that is
        /// the one the scan hook queued. A manual one is someone converting a book already
        /// in the library, and a need found there is shown, not acted on.
        /// <para>
        /// After the job is completed, not inside it: a repair shares the conversion's
        /// deduplication key, so an auto-repair offered while this job is still active
        /// would be refused as already queued.
        /// </para>
        /// </summary>
        internal async Task QueuePostConversionWorkAsync(ConversionJob job, IServiceProvider services)
        {
            if (job.Kind != ConversionJobKind.Convert || job.Trigger != ConversionTrigger.Automatic)
            {
                return;
            }

            try
            {
                var index = services.GetService<ILibraryTagIndexService>();
                if (index != null)
                {
                    await index.BuildAsync(refresh: false, CancellationToken.None, audiobookIds: [job.AudiobookId], planTrigger: TagTrigger.Import);
                }
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not judge the chapters of audiobook {AudiobookId} after its conversion", job.AudiobookId);
            }

            try
            {
                var auditor = services.GetService<IAudioAuditService>();
                if (auditor != null)
                {
                    await auditor.EnqueueAsync(job.AudiobookId, TagTrigger.Automatic, CancellationToken.None);
                }
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not offer audiobook {AudiobookId} to the audio audit after its conversion", job.AudiobookId);
            }

            try
            {
                var prober = services.GetService<IAudioQualityService>();
                if (prober != null)
                {
                    var result = await prober.EnqueueAsync(job.AudiobookId, TagTrigger.Import, CancellationToken.None);
                    if (result.Queued)
                    {
                        logger.LogInformation(
                            "Queued audio-quality probe {ProbeJobId} for audiobook {AudiobookId} after its conversion",
                            result.JobId,
                            job.AudiobookId);
                    }
                }
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not offer audiobook {AudiobookId} to the audio-quality probe after its conversion", job.AudiobookId);
            }
        }
    }
}
