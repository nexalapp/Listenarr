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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Conversion
{
    /// <summary>
    /// Reusing an encode a previous attempt already produced and verified.
    ///
    /// Split from the execution path because it is a self-contained question - is there
    /// a usable output from last time, and does it still match this book? - and keeping
    /// it there put the file past the size BackendArchitectureTests enforces.
    /// </summary>
    public sealed partial class ConversionJobProcessor
    {
        /// <summary>
        /// Reuse a verified encode from an earlier attempt, when there is one that still
        /// matches the book as it is now. Returns null when the encode must be run.
        ///
        /// The plan is rebuilt from the current sources before this is called, so
        /// verifying against it is what stops a stale output — produced before the book's
        /// files changed — from being published as though it were current.
        /// </summary>
        private async Task<ConversionResult?> TryReuseVerifiedOutputAsync(
            ConversionJob job,
            ConversionRequest request,
            IConversionQueueService queue,
            IAudiobookConverter converter,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(job.VerifiedOutputPath) || job.VerifiedOutputLength is null)
            {
                return null;
            }

            if (!string.Equals(job.VerifiedOutputPath, request.ScratchOutputPath, StringComparison.Ordinal)
                || !File.Exists(request.ScratchOutputPath))
            {
                await queue.ClearVerifiedOutputAsync(job.Id, cancellationToken);
                return null;
            }

            // Length first: it is free, and a truncated or replaced file is not the one
            // that was verified.
            long length;
            try
            {
                length = new FileInfo(request.ScratchOutputPath).Length;
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                await queue.ClearVerifiedOutputAsync(job.Id, cancellationToken);
                return null;
            }

            if (length != job.VerifiedOutputLength)
            {
                logger.LogInformation(
                    "Discarding the kept encode for conversion {JobId}: it is {Actual} bytes, not the {Expected} that were verified",
                    job.Id,
                    length,
                    job.VerifiedOutputLength);
                TryDeleteScratch(request.ScratchOutputPath);
                await queue.ClearVerifiedOutputAsync(job.Id, cancellationToken);
                return null;
            }

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.Verifying, 90, cancellationToken);

            var verification = await converter.VerifyExistingOutputAsync(request, cancellationToken);
            if (!verification.Success)
            {
                logger.LogInformation(
                    "Re-encoding conversion {JobId}: the kept encode no longer matches this book ({Reason})",
                    job.Id,
                    verification.Message);
                TryDeleteScratch(request.ScratchOutputPath);
                await queue.ClearVerifiedOutputAsync(job.Id, cancellationToken);
                return null;
            }

            logger.LogInformation(
                "Reusing the verified encode for conversion {JobId}; only publication is retried",
                job.Id);
            return verification;
        }

        /// <summary>
        /// Record a verified encode against the job so a retry can publish it directly.
        /// Best effort: failing to remember it costs an encode, never correctness.
        /// </summary>
        private async Task RememberVerifiedOutputAsync(
            ConversionJob job,
            string scratchPath,
            ConversionResult result,
            IConversionQueueService queue)
        {
            try
            {
                if (!File.Exists(scratchPath))
                {
                    return;
                }

                await queue.RecordVerifiedOutputAsync(
                    job.Id,
                    scratchPath,
                    new FileInfo(scratchPath).Length,
                    result.ChapterCount,
                    CancellationToken.None);
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                logger.LogDebug(ex, "Could not keep the verified encode for conversion {JobId}", job.Id);
                TryDeleteScratch(scratchPath);
            }
        }

        /// <summary>
        /// Persist one progress report, absorbing anything that goes wrong.
        ///
        /// <para>
        /// In its own scope: these run on the encoder's reader thread, and sharing the
        /// job scope's DbContext with the flow around them lets two operations overlap on
        /// one context, which EF refuses outright.
        /// </para>
        /// <para>
        /// Progress is a courtesy: it is derived from the encode rather than driving it,
        /// and the job's durable state does not depend on any single report landing.
        /// </para>
        /// </summary>
    }
}
