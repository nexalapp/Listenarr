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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Application.Audiobooks.Transcription;
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Audit
{
    /// <summary>
    /// The audio audit: the first minute of the book's first file and the last three
    /// quarters of a minute of its last, transcribed and judged against the record.
    ///
    /// <para>
    /// Those two stretches are where the credits live. A production reads the title,
    /// the author and often the narrator before the story and again after it, and
    /// nothing else in nine hours says what the book is. Both are heard so a file
    /// whose opening is trimmed still gets judged on its closing.
    /// </para>
    /// </summary>
    public sealed class AudioAuditService(
        IAudiobookRepository audiobookRepository,
        ITagQueueService tagQueue,
        IConfigurationService configurationService,
        IFileSystem fileSystem,
        ILogger<AudioAuditService> logger,
        ITranscriber? transcriber = null,
        TranscriptCache? transcripts = null,
        IAudiobookTagWriter? tagWriter = null) : IAudioAuditService
    {
        public static readonly TimeSpan OpeningWindow = TimeSpan.FromSeconds(90);
        public static readonly TimeSpan ClosingWindow = TimeSpan.FromSeconds(90);

        /// <summary>
        /// A final chapter no longer than this is the credits, not the story: the closing
        /// is heard from where it starts rather than from ninety seconds before the end.
        /// </summary>
        public static readonly TimeSpan CreditsChapterMaximum = TimeSpan.FromMinutes(3);

        public async Task<TagEnqueueResult> EnqueueAsync(int audiobookId, TagTrigger trigger, CancellationToken cancellationToken = default)
        {
            var settings = await configurationService.GetApplicationSettingsAsync();
            if (!settings.TranscriptionEnabled || transcriber == null)
            {
                return new TagEnqueueResult(
                    TagEnqueueOutcome.Disabled,
                    Reason: "Listening to a book means transcription. Turn it on in Settings → Metadata Tags.");
            }

            if (trigger == TagTrigger.Automatic && !settings.AudioAuditOnImport)
            {
                return new TagEnqueueResult(TagEnqueueOutcome.Disabled, Reason: "Auditing new imports is switched off.");
            }

            return await tagQueue.EnqueueAudioAuditAsync(audiobookId, trigger, cancellationToken);
        }

        public async Task<AudioAuditResult> AuditAsync(int audiobookId, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId)
                ?? throw new InvalidOperationException("That audiobook no longer exists.");

            if (transcriber == null || !await transcriber.IsAvailableAsync(cancellationToken))
            {
                throw new InvalidOperationException("Transcription is not available.");
            }

            var files = (audiobook.Files ?? [])
                .Where(file => FileUtils.IsAudioFile(file.Path ?? string.Empty))
                .Select(file => (File: file, FullPath: AudiobookFilePaths.ResolveFullPath(audiobook, file)))
                .Where(pair => pair.FullPath != null && fileSystem.FileExists(pair.FullPath))
                .OrderBy(pair => pair.File.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                throw new InvalidOperationException("This book has no audio files here to listen to.");
            }

            // Two stretches to hear, so two steps of progress; whisper gives no rate to
            // estimate from, and a bar that moves twice beats one that does not move.
            progress?.Report(0.1);
            var opening = await HearAsync(files[0].FullPath!, TimeSpan.Zero, OpeningWindow, cancellationToken);
            progress?.Report(0.55);

            var last = files[^1];
            string? closing = null;
            if (last.File.DurationSeconds is { } seconds && seconds > ClosingWindow.TotalSeconds + 5)
            {
                var duration = TimeSpan.FromSeconds(seconds);
                var (start, window) = await ClosingWindowFor(last.FullPath!, duration, cancellationToken);
                closing = await HearAsync(last.FullPath!, start, window, cancellationToken);
            }

            progress?.Report(0.9);

            // The closing is kept apart from the opening so the page can show which is which.
            var heard = string.IsNullOrWhiteSpace(closing)
                ? opening ?? string.Empty
                : $"{opening}{AudioAuditTranscript.ClosingMarker}{closing}";
            var settings = await configurationService.GetApplicationSettingsAsync();
            var aliases = AuthorAliases.Parse(settings.AuthorAliasesJson);

            var result = AudioIdentityMatcher.Judge(heard, audiobook.Title, audiobook.Authors, audiobook.Narrators, aliases);

            await audiobookRepository.SetAudioAuditAsync(
                audiobookId,
                new AudioAuditRecord(result.Verdict, result.Reason, heard, result.Credits, DateTime.UtcNow),
                cancellationToken);

            logger.LogInformation(
                "Audio audit of audiobook {AudiobookId}: {Verdict} (title {Title:P0}, author {Author:P0})",
                audiobookId,
                result.Verdict,
                result.TitleScore,
                result.AuthorScore);

            return result;
        }

        /// <summary>
        /// Where the closing credits are. Productions read them at the top of a short
        /// final chapter ("This has been a Hachette Audio production of…") as often as at
        /// the very end, so when the last chapter is short the window starts there.
        /// </summary>
        private async Task<(TimeSpan Start, TimeSpan Window)> ClosingWindowFor(string fullPath, TimeSpan duration, CancellationToken cancellationToken)
        {
            var fromEnd = (duration - ClosingWindow, ClosingWindow);
            if (tagWriter == null)
            {
                return fromEnd;
            }

            try
            {
                var tags = await tagWriter.ReadAsync(fullPath, cancellationToken);
                var lastChapter = tags.Chapters is { Count: > 1 } chapters ? chapters[^1] : null;
                if (lastChapter == null)
                {
                    return fromEnd;
                }

                var length = duration - lastChapter.Start;
                if (length <= TimeSpan.Zero || length > CreditsChapterMaximum)
                {
                    return fromEnd;
                }

                return (lastChapter.Start, length < ClosingWindow ? length : ClosingWindow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Could not read chapters of {Path} to place the closing window", LogRedaction.SanitizeFilePath(fullPath));
                return fromEnd;
            }
        }

        private async Task<string?> HearAsync(string fullPath, TimeSpan start, TimeSpan window, CancellationToken cancellationToken)
        {
            long length = 0;
            var lastWrite = DateTime.MinValue;
            try
            {
                length = fileSystem.GetFileLength(fullPath);
                lastWrite = fileSystem.GetLastWriteTimeUtc(fullPath);
                var cached = transcripts?.TryGet(fullPath, length, lastWrite, start, window);
                if (cached != null)
                {
                    return cached.Text;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not stat {Path} for the transcript cache", LogRedaction.SanitizeFilePath(fullPath));
            }

            var transcript = await transcriber!.TranscribeAsync(fullPath, start, window, cancellationToken);
            if (length > 0)
            {
                transcripts?.Set(fullPath, length, lastWrite, start, window, transcript);
            }

            return transcript.Text;
        }
    }
}
