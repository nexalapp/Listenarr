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
using Listenarr.Application.Audiobooks.Transcription;
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.FoundBooks.Services
{
    public sealed record FoundBookMatchChoice(
        string? Asin,
        string? Title,
        string? Author,
        string? Source,
        string? ImageUrl,
        double? Confidence);

    public sealed record FoundBookListenResult(AudioCredits Credits, string Transcript);

    public interface IFoundBookMatchService
    {
        /// <summary>Remember a catalogue match on the row; null clears it.</summary>
        Task<FoundBook?> SetMatchAsync(int id, FoundBookMatchChoice? choice, CancellationToken cancellationToken = default);

        /// <summary>
        /// Hear the opening of the book's first file and read the spoken credits out of
        /// it. Listens whatever the transcription setting says — the setting governs
        /// listening to every scanned book, not identifying one whose tags say nothing.
        /// Throws <see cref="TranscriptionUnavailableException"/> while the model downloads.
        /// </summary>
        Task<FoundBookListenResult?> ListenAsync(int id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The match a row carries, and the ear that helps choose one. Nothing here moves a
    /// file; a match is a note on the row until Import acts on it.
    /// </summary>
    public sealed class FoundBookMatchService(
        IFoundBookRepository repository,
        IConfigurationService configurationService,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger<FoundBookMatchService> logger,
        ITranscriber? transcriber = null,
        TranscriptCache? transcripts = null) : IFoundBookMatchService
    {
        public async Task<FoundBook?> SetMatchAsync(int id, FoundBookMatchChoice? choice, CancellationToken cancellationToken = default)
        {
            var updated = await repository.UpdateAsync(id, row =>
            {
                row.MatchAsin = Clip(choice?.Asin, 32);
                row.MatchTitle = Clip(choice?.Title, 500);
                row.MatchAuthor = Clip(choice?.Author, 500);
                row.MatchSource = Clip(choice?.Source, 64);
                row.MatchImageUrl = Clip(choice?.ImageUrl, 2000);
                row.MatchConfidence = choice?.Confidence is { } c ? Math.Clamp(c, 0, 1) : null;
            }, cancellationToken);
            return updated ? await repository.GetAsync(id, cancellationToken) : null;
        }

        public async Task<FoundBookListenResult?> ListenAsync(int id, CancellationToken cancellationToken = default)
        {
            var row = await repository.GetAsync(id, cancellationToken);
            if (row == null)
            {
                return null;
            }

            var settings = await configurationService.GetApplicationSettingsAsync();
            if (transcriber == null)
            {
                throw new InvalidOperationException("Transcription is not available in this host.");
            }

            if (!await transcriber.IsAvailableAsync(TranscriptionPolicy.Always, cancellationToken))
            {
                throw new TranscriptionUnavailableException("The whisper model is still downloading; try again in a minute.");
            }

            var first = FoundBookFilesJson.Deserialize(row.FilesJson).FirstOrDefault(f => f.IsAudio && fileSystem.FileExists(f.Path))
                ?? throw new InvalidOperationException("This book has no audio file here to listen to.");

            var model = ChapterPlanKeys.ModelFor(transcriptionEnabled: true, settings.TranscriptionModel);
            var text = await HearAsync(first.Path, AudioAuditService.OpeningWindow, model, cancellationToken);
            var credits = AudioCreditsParser.Parse(text);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            await repository.UpdateAsync(id, r =>
            {
                r.HeardTitle = Clip(credits.Title, 500);
                r.HeardAuthor = Clip(credits.Author, 500);
                r.HeardNarrator = Clip(credits.Narrator, 500);
                r.HeardTranscript = text;
                r.HeardAt = now;
            }, cancellationToken);

            logger.LogInformation(
                "Listened to found book {Id}: title {Title}, author {Author}, narrator {Narrator}",
                id,
                credits.Title ?? "-",
                credits.Author ?? "-",
                credits.Narrator ?? "-");

            return new FoundBookListenResult(credits, text);
        }

        private async Task<string> HearAsync(string path, TimeSpan window, string? model, CancellationToken cancellationToken)
        {
            long length = 0;
            var lastWrite = DateTime.MinValue;
            try
            {
                length = fileSystem.GetFileLength(path);
                lastWrite = fileSystem.GetLastWriteTimeUtc(path);
                var cached = transcripts?.TryGet(path, length, lastWrite, TimeSpan.Zero, window, model);
                if (cached != null)
                {
                    return cached.Text;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not stat {Path} for the transcript cache", path);
            }

            var transcript = await transcriber!.TranscribeAsync(path, TimeSpan.Zero, window, TranscriptionPolicy.Always, cancellationToken);
            if (length > 0)
            {
                transcripts?.Set(path, length, lastWrite, TimeSpan.Zero, window, transcript, model);
            }

            return transcript.Text;
        }

        private static string? Clip(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
    }
}
