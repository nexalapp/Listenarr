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
using Listenarr.Application.Common.Exceptions;
using Listenarr.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Conversion
{
    public sealed partial class ConversionJobProcessor
    {
        private async Task<ExecutionOutcome> ExecuteAsync(
            ConversionJob job,
            IServiceProvider services,
            IConversionQueueService queue,
            CancellationToken cancellationToken)
        {
            var audiobookRepository = services.GetRequiredService<IAudiobookRepository>();
            var audiobook = await audiobookRepository.GetByIdAsync(job.AudiobookId);
            if (audiobook == null)
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.SourceUnreadable,
                    "That audiobook no longer exists.");
            }

            var sources = CollectSourceFiles(audiobook);
            if (sources.Count == 0)
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.SourceUnreadable,
                    "This book no longer has any MP3 files to convert.");
            }

            // A conversion rewrites what the library serves for this book, so it must not
            // run while a move owns the book's filesystem state. Transient by design: the
            // move finishes, and the retry then finds the files where the move left them.
            try
            {
                await services.GetRequiredService<IMoveQueueService>()
                    .EnsureFilesystemMutationAllowedAsync(job.AudiobookId, cancellationToken);
            }
            catch (ApplicationConflictException conflict)
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.Transient,
                    conflict.SafeDetail);
            }

            var semanticsResolver = services.GetRequiredService<IFileSystemSemanticsResolver>();
            var pathComparer = await ResolvePathComparerAsync(
                semanticsResolver,
                audiobook,
                cancellationToken);

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.Probing, 0, cancellationToken);

            var ffmpegService = services.GetRequiredService<IFfmpegService>();
            var planning = await BuildPlanAsync(
                audiobook,
                sources.Select(s => s.File).ToList(),
                ffmpegService,
                pathComparer,
                cancellationToken);

            if (!planning.Success)
            {
                return ExecutionOutcome.Failed(planning.FailureKind, planning.Error!);
            }

            var plan = planning.Plan!;
            var settings = await services.GetRequiredService<IConfigurationService>()
                .GetApplicationSettingsAsync();

            var destinationPath = await BuildDestinationPathAsync(
                audiobook,
                planning.Tags!,
                services,
                settings);

            if (destinationPath == null)
            {
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.Unknown,
                    "The converted file's destination could not be determined because this book has no known folder.");
            }

            // The encode writes outside the library, and publication then imports the
            // result the same way a completed download is imported. The library
            // filesystem carries no scratch namespace by design, and a partly written
            // encode sitting next to the book is exactly what that rule exists to
            // prevent — a reader cannot tell it from a real file.
            var scratchDirectory = services.GetRequiredService<IApplicationPathService>()
                .ResolveFromConfig("conversion");
            Directory.CreateDirectory(scratchDirectory);

            var scratchPath = Path.Combine(scratchDirectory, $"conversion-{job.Id:N}.m4b");

            var converter = services.GetRequiredService<IAudiobookConverter>();
            // ffmpeg reports about once a second, so an hour-long book would otherwise
            // write the job row and broadcast roughly 3,600 times to move a bar that has
            // 100 positions. Report only when the whole percent actually changes.
            var lastReportedPercent = -1;
            var progress = new Progress<ConversionProgress>(report =>
            {
                var percent = (int)Math.Round(Math.Clamp(report.Fraction, 0, 1) * 100);
                if (percent == Interlocked.Exchange(ref lastReportedPercent, percent))
                {
                    return;
                }

                // Fire-and-forget: progress that could not be persisted must never stall
                // or fail the encode reporting it. A report can also land after the job
                // has finished and its scope has been disposed, so nothing here may throw
                // into an unobserved task.
                _ = ReportProgressSafelyAsync(queue, job.Id, percent);
            });

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.Encoding, 0, cancellationToken);

            ConversionResult result;
            try
            {
                result = await converter.ConvertAsync(
                    new ConversionRequest(plan, scratchPath, planning.Tags!),
                    progress,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // A cancelled encode leaves a partial file behind; nothing has been
                // published, so removing it is safe and keeps the library folder clean.
                TryDeleteScratch(scratchPath);
                throw;
            }

            if (!result.Success)
            {
                TryDeleteScratch(scratchPath);
                return ExecutionOutcome.Failed(result.FailureKind, result.Message ?? "The conversion failed.");
            }

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.Publishing, 95, cancellationToken);

            PublicationOutcome publication;
            try
            {
                publication = await PublishConvertedFileAsync(
                    job.Id,
                    audiobook,
                    scratchPath,
                    destinationPath,
                    services.GetRequiredService<IFileMover>(),
                    services.GetRequiredService<IAudiobookFileService>(),
                    cancellationToken);
            }
            catch
            {
                // The scratch file is several hundred megabytes for a real book, and one
                // is written per attempt. Losing it on an unexpected path would fill the
                // config volume a retry at a time.
                TryDeleteScratch(scratchPath);
                throw;
            }

            if (!publication.Success)
            {
                TryDeleteScratch(scratchPath);
                return ExecutionOutcome.Failed(
                    ConversionFailureKind.OutputRejected,
                    publication.Error!);
            }

            await queue.ReportProgressAsync(job.Id, ConversionJobPhase.RetiringSources, 98, cancellationToken);

            var warning = await RetireSourcesAsync(
                audiobook,
                sources.Select(s => new SourceFileReference(s.File.Id, s.FullPath)).ToList(),
                settings,
                await services.GetRequiredService<IRootFolderService>().GetAllAsync(),
                services.GetRequiredService<IAudiobookFileRepository>(),
                services.GetRequiredService<IFileSystem>(),
                cancellationToken);

            return ExecutionOutcome.Succeeded(
                publication.OutputPath!,
                result.ChapterCount,
                audiobook.Title,
                warning);
        }

        /// <summary>
        /// Persist one progress report, absorbing anything that goes wrong.
        ///
        /// Progress is a courtesy: it is derived from the encode rather than driving it,
        /// and the job's durable state does not depend on any single report landing.
        /// </summary>
        private async Task ReportProgressSafelyAsync(
            IConversionQueueService queue,
            Guid jobId,
            int percent)
        {
            try
            {
                await queue.ReportProgressAsync(
                    jobId,
                    ConversionJobPhase.Encoding,
                    percent,
                    CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Could not record progress for conversion {JobId}", jobId);
            }
        }

        /// <summary>
        /// Where the M4B lands: the book's own directory, named from the configured file
        /// naming pattern. Conversion replaces a book's files; it does not relocate it.
        /// </summary>
        private static async Task<string?> BuildDestinationPathAsync(
            Audiobook audiobook,
            AudioMetadata tags,
            IServiceProvider services,
            ApplicationSettings settings)
        {
            if (string.IsNullOrWhiteSpace(audiobook.BasePath))
            {
                return null;
            }

            var namingService = services.GetRequiredService<IFileNamingService>();

            string fileName;
            try
            {
                fileName = namingService.ApplyNamingPattern(
                    settings.FileNamingPattern,
                    tags,
                    treatAsFilename: true);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                fileName = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = FileUtils.SafeFileName(
                    audiobook.Title is { Length: > 0 } title ? title : $"audiobook-{audiobook.Id}");
            }

            return await Task.FromResult(
                Path.GetFullPath(Path.Combine(audiobook.BasePath, fileName + ".m4b")));
        }

        /// <summary>The book's MP3 files, with their paths resolved.</summary>
        private static List<(AudiobookFile File, string FullPath)> CollectSourceFiles(Audiobook audiobook)
        {
            var results = new List<(AudiobookFile, string)>();
            foreach (var file in audiobook.Files ?? [])
            {
                if (string.IsNullOrWhiteSpace(file.Path)
                    || !string.Equals(Path.GetExtension(file.Path), ".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fullPath = ResolveFullPath(audiobook, file);
                if (fullPath != null)
                {
                    results.Add((file, fullPath));
                }
            }

            return results;
        }

        /// <summary>
        /// Ordering has to use the source filesystem's own case semantics, or two files
        /// differing only in case would be treated as one on a case-sensitive share.
        /// </summary>
        private static async Task<StringComparer> ResolvePathComparerAsync(
            IFileSystemSemanticsResolver resolver,
            Audiobook audiobook,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(audiobook.BasePath))
                {
                    var resolution = await resolver.ResolveAsync(
                        audiobook.BasePath,
                        FileSystemCaseSensitivityMode.Auto,
                        cancellationToken);
                    if (resolution.Semantics.Comparer is { } comparer)
                    {
                        return comparer;
                    }
                }
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                // Fall through to the conservative default below.
            }

            // Ordinal treats more paths as distinct, which is the safe direction: it can
            // list a duplicate, never silently drop a chapter.
            return StringComparer.Ordinal;
        }
    }
}
