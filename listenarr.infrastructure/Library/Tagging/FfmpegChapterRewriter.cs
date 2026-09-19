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
using System.Diagnostics;
using System.Text;
using Listenarr.Application.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Chapters;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Tagging
{
    /// <summary>
    /// Rewrites a container's chapters by remuxing through ffmpeg, then puts back the
    /// tags the remux dropped and proves the result.
    ///
    /// <para>
    /// The verification is the same standard the tag writer holds itself to, plus the
    /// chapters: the audio's duration is unchanged, the cover survived, every tag the
    /// source carried reads back, the played chapter list matches the plan, and the
    /// Nero atom parses — which is the whole reason the file was here.
    /// </para>
    /// </summary>
    public sealed class FfmpegChapterRewriter(
        IFfmpegService ffmpegService,
        IProcessRunner processRunner,
        IAudiobookTagWriter tagWriter,
        FfprobeTagReader reader,
        ILogger<FfmpegChapterRewriter> logger) : IChapterRewriter
    {
        private const int RemuxTimeoutMs = 20 * 60 * 1000;
        private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MarkTolerance = TimeSpan.FromMilliseconds(500);

        public async Task<TagWriteResult> RewriteAsync(
            ChapterRewriteRequest request,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var ffmpegPath = await ffmpegService.GetFfmpegPathAsync();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.WriterUnavailable,
                    "No ffmpeg is installed, so chapters cannot be rewritten.");
            }

            if (!File.Exists(request.SourcePath))
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.SourceUnreadable,
                    $"Source file is missing: {LogRedaction.SanitizeFilePath(request.SourcePath)}");
            }

            if (request.Plan.Chapters.Count == 0)
            {
                return TagWriteResult.Fail(TagWriteFailureKind.OutputRejected, "The chapter plan is empty.");
            }

            var documentPath = Path.Combine(
                Path.GetDirectoryName(request.ScratchOutputPath) ?? Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(request.ScratchOutputPath) + ".chapters.ffmetadata");

            try
            {
                await File.WriteAllTextAsync(
                    documentPath,
                    FfmpegCommandBuilder.BuildChapterDocument(request.Plan.Chapters),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);

                progress?.Report(0.05);
                var run = await RunAsync(
                    ffmpegPath,
                    FfmpegCommandBuilder.BuildChapterRemuxArguments(request.SourcePath, documentPath, request.ScratchOutputPath),
                    cancellationToken);
                if (run.TimedOut)
                {
                    return TagWriteResult.Fail(TagWriteFailureKind.Transient, "ffmpeg did not finish the remux in time.");
                }

                if (run.ExitCode != 0)
                {
                    return TagWriteResult.Fail(
                        TagWriteFailureKind.WriteFailed,
                        $"ffmpeg exited with {run.ExitCode}: {FfmpegService.SummariseFfprobeFailure(run.Stderr)}");
                }

                progress?.Report(0.6);

                // Put back what the mov muxer dropped. Only the catalog's tags: the
                // container's own bookkeeping (brand, encoder) is the muxer's to write.
                var tags = request.Existing.Tags
                    .Where(pair => !TagCatalog.ContainerTags.Contains(pair.Key) && !string.IsNullOrEmpty(pair.Value))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                if (tags.Count > 0)
                {
                    var applied = await tagWriter.ApplyAsync(request.ScratchOutputPath, tags, cancellationToken);
                    if (!applied.Success)
                    {
                        return applied;
                    }
                }

                progress?.Report(0.85);
                var verified = await VerifyAsync(request, tags, cancellationToken);
                progress?.Report(1);
                return verified;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Chapter rewrite failed on the filesystem");
                return TagWriteResult.Fail(TagWriteFailureKind.Transient, ex.Message);
            }
            finally
            {
                TryDelete(documentPath);
            }
        }

        private async Task<ProcessResult> RunAsync(
            string ffmpegPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return await processRunner.RunAsync(startInfo, RemuxTimeoutMs, cancellationToken);
        }

        private async Task<TagWriteResult> VerifyAsync(
            ChapterRewriteRequest request,
            IReadOnlyDictionary<string, string> tags,
            CancellationToken cancellationToken)
        {
            var output = request.ScratchOutputPath;
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
            {
                return TagWriteResult.Fail(TagWriteFailureKind.OutputRejected, "ffmpeg reported success but wrote no output.");
            }

            var probe = await reader.ProbeAsync(output, cancellationToken);
            if (probe.Tags == null)
            {
                return TagWriteResult.Fail(TagWriteFailureKind.OutputRejected, $"The rewritten file could not be read back: {probe.Error}");
            }

            var written = probe.Tags;
            var plan = request.Plan.Chapters;

            if (written.ChapterCount != plan.Count || written.Chapters == null)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    $"The rewritten file plays {written.ChapterCount} chapter(s) but the plan has {plan.Count}.");
            }

            for (var index = 0; index < plan.Count; index++)
            {
                var drift = (written.Chapters[index].Start - plan[index].Start).Duration();
                if (drift > MarkTolerance)
                {
                    return TagWriteResult.Fail(
                        TagWriteFailureKind.OutputRejected,
                        $"Chapter {index + 1} landed {drift.TotalMilliseconds:F0}ms away from where it was planned.");
                }
            }

            var existing = request.Existing;
            if (existing.Duration > TimeSpan.Zero && (written.Duration - existing.Duration).Duration() > DurationTolerance)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    $"The rewritten file is {written.Duration} long but the original is {existing.Duration}.");
            }

            if (existing.HasCoverArt && !written.HasCoverArt)
            {
                return TagWriteResult.Fail(TagWriteFailureKind.OutputRejected, "The rewrite lost the cover art.");
            }

            foreach (var (key, value) in tags)
            {
                if (!written.Tags.TryGetValue(key, out var actual) || !TagValue.AreEquivalent(value, actual))
                {
                    return TagWriteResult.Fail(
                        TagWriteFailureKind.OutputRejected,
                        $"The tag '{key}' did not survive the rewrite.");
                }
            }

            ChapterAtomState? atoms;
            try
            {
                atoms = ChapterAtomInspector.Inspect(output);
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException)
            {
                return TagWriteResult.Fail(TagWriteFailureKind.OutputRejected, $"The rewritten file's atoms could not be inspected: {ex.Message}");
            }

            if (atoms is not { HasNeroAtom: true, NeroAtomError: null } || atoms.NeroChapterCount != plan.Count)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    atoms?.NeroAtomError is { } error
                        ? $"The rewritten chapter atom does not parse: {error}"
                        : "The rewritten file carries no sound chapter atom.");
            }

            logger.LogInformation(
                "Chapter rewrite verified: {Chapters} chapter(s) from {Source}, {Tags} tag(s) restored",
                plan.Count,
                request.Plan.Source,
                tags.Count);
            return TagWriteResult.Ok(plan.Count);
        }

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not delete a chapter document");
            }
        }
    }

    /// <summary>Hands the inspector's shifted-atom recovery to the application layer.</summary>
    public sealed class ChapterAtomRecovery : IChapterAtomRecovery
    {
        public IReadOnlyList<EmbeddedChapter>? TryRecover(string path) => ChapterAtomInspector.TryRecoverShifted(path);
    }
}
