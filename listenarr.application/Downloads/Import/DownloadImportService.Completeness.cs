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
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Import
{
    public partial class DownloadImportService
    {
        /// <summary>
        /// Verdicts that stop the batch. <see cref="FoundBookCompleteness.Unknown"/> is
        /// absent on purpose: on this path it means the files declared no total and the
        /// record carries no runtime, so there is no evidence either way, and refusing on
        /// none of it would strand ordinary imports.
        /// </summary>
        private static readonly FoundBookCompleteness[] RefusedCompleteness =
        [
            FoundBookCompleteness.Corrupt,
            FoundBookCompleteness.Incomplete,
            FoundBookCompleteness.Overrun
        ];

        /// <summary>
        /// Judge the audio against the book before a single file is moved, and refuse the
        /// batch rather than import something that is not the book.
        /// </summary>
        /// <remarks>
        /// The found-on-disk scanner has decided this question since it existed, and the
        /// download path never asked it — so a release holding a whole series imported as
        /// one book, and a release missing its last part imported as a complete one. The
        /// same <see cref="FoundBookCompletenessAnalyzer"/> decides both, because two
        /// answers to "is this the whole book" would eventually disagree.
        ///
        /// This path knows one thing the scanner cannot: the record's own
        /// <see cref="Audiobook.Runtime"/>. The scanner has no record yet and must rely on
        /// a declared length inside the files or beside them. Here the runtime is the
        /// better source and the files' own declaration is the fallback, which is what
        /// makes an over-run detectable at all.
        ///
        /// Returns <c>null</c> when the import may proceed.
        /// </remarks>
        private async Task<ImportResult?> RefuseIncompleteBatchAsync(
            Audiobook audiobook,
            IReadOnlyList<PlannedImportFile> plannedAudioFiles,
            ApplicationSettings settings,
            CancellationToken ct)
        {
            if (!settings.GateImportOnCompleteness || plannedAudioFiles.Count == 0 || foundBookProbe == null)
            {
                return null;
            }

            var observations = new List<FoundBookAudioObservation>(plannedAudioFiles.Count);
            double? declaredFromFiles = null;

            foreach (var plan in plannedAudioFiles)
            {
                ct.ThrowIfCancellationRequested();

                FoundBookProbeSnapshot probe;
                try
                {
                    probe = await foundBookProbe.ProbeAsync(plan.FullPath, ct);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A probe that throws is not evidence the book is wrong, and failing the
                    // import on it would make a broken ffprobe look like a bad release. Let
                    // the batch through and say why it was not judged.
                    logger.LogWarning(
                        exception,
                        "Could not probe {Path} for audiobook {AudiobookId}; importing without a completeness check",
                        LogRedaction.SanitizeFilePath(plan.FullPath),
                        audiobook.Id);
                    return null;
                }

                declaredFromFiles ??= probe.DeclaredDurationSeconds;

                observations.Add(new FoundBookAudioObservation(
                    Path.GetFileName(plan.FullPath),
                    !probe.Succeeded,
                    probe.DurationSeconds,
                    probe.ChapterCount,
                    probe.TrackNumber,
                    probe.TrackTotal,
                    plan.ChapterNumberHint));
            }

            // Runtime is minutes on the record and seconds everywhere in the analyzer.
            var declared = audiobook.Runtime is > 0
                ? audiobook.Runtime.Value * 60.0
                : declaredFromFiles;

            var verdict = FoundBookCompletenessAnalyzer.Analyze(observations, declared);
            if (!RefusedCompleteness.Contains(verdict.Completeness))
            {
                return null;
            }

            logger.LogWarning(
                "Refusing import for audiobook {AudiobookId} ({Title}): {Completeness} — {Reason}",
                audiobook.Id,
                audiobook.Title,
                verdict.Completeness,
                verdict.Reason);

            return ImportResult.Rejected(
                $"{verdict.Completeness}: {verdict.Reason} Left in the download directory for a manual import.");
        }
    }
}
