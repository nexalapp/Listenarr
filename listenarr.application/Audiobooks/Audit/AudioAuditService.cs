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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Application.Audiobooks.Transcription;
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Audiobooks.Chapters;
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
        IAudiobookTagWriter? tagWriter = null,
        ISilenceDetector? silenceDetector = null) : IAudioAuditService
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

            if (transcriber == null)
            {
                throw new InvalidOperationException("Transcription is not available.");
            }

            if (!await transcriber.IsAvailableAsync(cancellationToken))
            {
                // Enqueue refused while transcription was off, so this is the model still
                // on its way down — worth coming back to, not a verdict.
                throw new TranscriptionUnavailableException("The whisper model is still downloading; the audit will be tried again when it has landed.");
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

            var settings = await configurationService.GetApplicationSettingsAsync();
            var model = ChapterPlanKeys.ModelFor(settings.TranscriptionEnabled, settings.TranscriptionModel);

            var aliasesJson = settings.AuthorAliasesJson;

            // A transcript already on the record is reused when no file has changed since
            // it was taken. Listening costs minutes of CPU and the words do not change;
            // re-judging them does, every time the credits parser learns something. That
            // is what makes a re-run over a whole library affordable.
            //
            // The offset the credits were found at is part of that identity, so it is read
            // back rather than worked out again - otherwise the identity rebuilt here could
            // never equal the one on the record, and the book paid for a fresh transcription
            // on every audit to arrive at words it already had.
            var recorded = AudioAuditFileIdentity.OpeningSkipOf(audiobook.AudioAuditFileIdentity);
            var identity = CurrentFileIdentity(files, model, recorded);
            if (StoredTranscriptIsUsable(audiobook, identity, out var stored))
            {
                logger.LogInformation(
                    "Audio audit of audiobook {AudiobookId}: re-judging the transcript already on record",
                    audiobookId);
                progress?.Report(0.9);
                return await JudgeAndSaveAsync(
                    audiobookId, audiobook, stored!, aliasesJson, identity, cancellationToken);
            }

            // Two stretches to hear, so two steps of progress; whisper gives no rate to
            // estimate from, and a bar that moves twice beats one that does not move.
            //
            // Always from the very start. The opening window used to begin after the shop's
            // ident, which meant the first thing said was never heard and had to be salvaged
            // from whatever was on the record - a book whose opening line is its own title
            // lost the title that way, and re-listening stacked copies of the passage it had
            // just re-read. The credits are hunted for separately below, in windows of their
            // own, so this one has no reason to skip anything.
            progress?.Report(0.1);
            var heardOpening = await HearAsync(files[0].FullPath!, TimeSpan.Zero, OpeningWindow, model, cancellationToken);

            // What came back names neither the book nor anyone who made it, so the credits
            // are in there somewhere and whisper did not read them. Hunt: short windows,
            // walking in, stopping at the first that reads like credits.
            //
            // One offset is not enough, and the obvious one is wrong more often than not. The
            // pause rule stops where the ident ends, and on The Barsoom Project that is
            // exactly where nine seconds of music begin, so every window from there comes
            // back as "[music]" and nothing else - at seventeen seconds, at thirty, at
            // eighty-eight. Eight seconds further in, with the music behind it, whisper reads
            // "Audible Frontiers presents The Barsoom Project. Written by Larry Niven and
            // Stephen Barnes and narrated by Stefan Rudnicki": the title, both authors and
            // the narrator, none of which is spoken anywhere else in the recording.
            var found = TimeSpan.Zero;
            if (OpeningIdent.LooksSwallowed(heardOpening, audiobook.Title, audiobook.Authors))
            {
                var afterIdent = await OpeningStartAsync(files[0].FullPath!, cancellationToken);
                foreach (var at in OpeningIdent.CreditsProbes(afterIdent))
                {
                    // Recorded as it is tried, so that a hunt which finds nothing is still a
                    // hunt that happened. Left off the identity it would be run again on
                    // every audit, for a book already known to have nothing to find.
                    found = at;
                    var credits = await HearAsync(files[0].FullPath!, at, OpeningIdent.CreditsWindow, model, cancellationToken);

                    // Only taken when it really found them. A book that simply opens on its
                    // story has nothing to find, and a window that merely starts later is
                    // how 2001: A Space Odyssey lost its title. What it found goes in front,
                    // because the parser takes the first thing that reads like a credit.
                    if (AudioCreditsParser.LooksLikeOpeningCredits(credits))
                    {
                        heardOpening = string.IsNullOrWhiteSpace(heardOpening)
                            ? credits
                            : credits + "\n" + heardOpening;
                        break;
                    }
                }
            }

            progress?.Report(0.55);

            var last = files[^1];
            string? closing = null;
            if (last.File.DurationSeconds is { } seconds && seconds > ClosingWindow.TotalSeconds + 5)
            {
                var duration = TimeSpan.FromSeconds(seconds);
                var (start, window) = await ClosingWindowFor(last.FullPath!, duration, cancellationToken);
                closing = await HearAsync(last.FullPath!, start, window, model, cancellationToken);
            }

            progress?.Report(0.9);

            // The closing is kept apart from the opening so the page can show which is which.
            var heard = string.IsNullOrWhiteSpace(closing)
                ? heardOpening ?? string.Empty
                : $"{heardOpening}{AudioAuditTranscript.ClosingMarker}{closing}";
            // Measured again after listening: the files are what the transcript describes
            // as of now, not as of before a long transcription.
            return await JudgeAndSaveAsync(
                audiobookId,
                audiobook,
                heard,
                aliasesJson,
                CurrentFileIdentity(files, model, found),
                cancellationToken);
        }

        /// <summary>
        /// What the two files the audit listens to look like now, or null when either
        /// cannot be measured. Only the first and the last are heard, so only those two
        /// decide whether a stored transcript still describes the book.
        /// </summary>
        /// <summary>
        /// How far into the first file to start listening. A shop's ident in front of the
        /// credits makes whisper swallow everything after it, so the window begins at the
        /// end of the first pause when there is one. Costs one decode of a few seconds.
        /// </summary>
        private async Task<TimeSpan> OpeningStartAsync(string fullPath, CancellationToken cancellationToken)
        {
            if (silenceDetector == null)
            {
                return TimeSpan.Zero;
            }

            try
            {
                var pauses = await silenceDetector.DetectAsync(
                    fullPath,
                    OpeningIdent.ShortestGap,
                    OpeningIdent.MostToSkip + TimeSpan.FromSeconds(2),
                    cancellationToken);
                var afterPause = OpeningIdent.StartsAfter(pauses);

                // Music has no pauses, so a book that opens on a bed of it offers nothing
                // to start after. Start past it instead; the credits are usually read over
                // the tail of the music and whisper hears them once the window does not
                // begin in it.
                return afterPause > TimeSpan.Zero ? afterPause : OpeningIdent.PastAnIntro;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not look for an ident at the start of {Path}", LogRedaction.SanitizeFilePath(fullPath));
                return TimeSpan.Zero;
            }
        }

        private string? CurrentFileIdentity(IReadOnlyList<(AudiobookFile File, string? FullPath)> files, string? model, TimeSpan opening)
        {
            try
            {
                var heard = files.Count == 1 ? new[] { files[0] } : [files[0], files[^1]];
                var parts = new List<(long, DateTime)>(heard.Length);
                foreach (var (_, path) in heard)
                {
                    if (path == null)
                    {
                        return null;
                    }

                    parts.Add((fileSystem.GetFileLength(path), fileSystem.GetLastWriteTimeUtc(path)));
                }

                return AudioAuditFileIdentity.Of(parts, model, opening.TotalSeconds);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not measure the files behind a stored transcript");
                return null;
            }
        }

        /// <summary>
        /// Whether the words on the record can stand in for listening again: there is a
        /// transcript, and the files it was taken from are byte-for-byte the size they
        /// were, written when they were. A recording swapped in keeps neither.
        /// </summary>
        private static bool StoredTranscriptIsUsable(Audiobook audiobook, string? identity, out string? transcript)
        {
            transcript = audiobook.AudioAuditHeard;
            return !string.IsNullOrWhiteSpace(transcript)
                && AudioAuditFileIdentity.Matches(audiobook.AudioAuditFileIdentity, identity);
        }

        private async Task<AudioAuditResult> JudgeAndSaveAsync(
            int audiobookId,
            Audiobook audiobook,
            string heard,
            string? aliasesJson,
            string? fileIdentity,
            CancellationToken cancellationToken)
        {
            var aliases = AuthorAliases.Parse(aliasesJson);
            var known = await audiobookRepository.GetKnownNarratorsAsync(cancellationToken);
            var result = AudioIdentityMatcher.Judge(
                heard,
                audiobook.Title,
                audiobook.Authors,
                audiobook.Narrators,
                aliases,
                known);
            var credits = result.Credits;

            await audiobookRepository.SetAudioAuditAsync(
                audiobookId,
                new AudioAuditRecord(result.Verdict, result.Reason, heard, credits, DateTime.UtcNow, fileIdentity),
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

                // The whole of it, not the first ninety seconds of it. Capping the window
                // at ninety while starting at the chapter's beginning stops the transcript
                // short of the file's end - The Lost World's final chapter runs 161s, so
                // the window ended 71 seconds early, mid-sentence, and the completeness
                // check read our own truncation as the book being cut off. Whatever the
                // window starts at, it has to reach the end.
                return (lastChapter.Start, length);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogDebug(ex, "Could not read chapters of {Path} to place the closing window", LogRedaction.SanitizeFilePath(fullPath));
                return fromEnd;
            }
        }

        private async Task<string?> HearAsync(string fullPath, TimeSpan start, TimeSpan window, string? model, CancellationToken cancellationToken)
        {
            long length = 0;
            var lastWrite = DateTime.MinValue;
            try
            {
                length = fileSystem.GetFileLength(fullPath);
                lastWrite = fileSystem.GetLastWriteTimeUtc(fullPath);
                var cached = transcripts?.TryGet(fullPath, length, lastWrite, start, window, model);
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
                transcripts?.Set(fullPath, length, lastWrite, start, window, transcript, model);
            }

            return transcript.Text;
        }
    }
}
