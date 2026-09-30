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
using System.Globalization;
using Listenarr.Application.Audiobooks.Transcription;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Transcription
{
    /// <summary>
    /// The half of the transcriber that deals with whisper giving up part way through a
    /// window. See <see cref="TranscriptStall"/> for what that looks like and why nothing
    /// downstream can spot it.
    /// </summary>
    public sealed partial class WhisperTranscriber
    {
        /// <summary>
        /// Listens to the stretch after a stall and puts it in place of what was returned
        /// for it. Once only: a second listen that stalls again has nothing left to try, and
        /// the guard is what stops a window with a long quiet tail decoding forever.
        /// </summary>
        private async Task<Transcript> HearTheRestAsync(
            IReadOnlyList<HeardSpan> heard,
            string modelPath,
            string path,
            TimeSpan start,
            TimeSpan length,
            CancellationToken cancellationToken)
        {
            if (TranscriptStall.FirstStall(heard, length) is not { } from)
            {
                return Transcript.FromSegments(heard.Select(span => span.Text));
            }

            var kept = heard.Where(span => span.End <= from).ToList();
            var again = await DecodeAsync(path, start + from, length - from, cancellationToken);
            if (again.Length == 0)
            {
                return Transcript.FromSegments(heard.Select(span => span.Text));
            }

            var rest = await ListenAsync(again, modelPath, path, start + from, length - from, cancellationToken);

            // Only taken when it is more than what it replaces. A stall can be whisper
            // refusing audio it cannot read at all, and then the second listen comes back
            // with less than the first - dropping the stalled phrase would lose words that,
            // garbled as they are, may still name the book.
            var replaced = Words(heard.Where(span => span.End > from));
            var found = Words(rest);
            if (found <= replaced)
            {
                return Transcript.FromSegments(heard.Select(span => span.Text));
            }

            logger.LogInformation(
                "whisper stalled {From}s into a {Length}s window of {Path}; listening again found {Found} words where it had {Replaced}",
                from.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture),
                length.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture),
                LogRedaction.SanitizeFilePath(path),
                found,
                replaced);

            return Transcript.FromSegments(kept.Concat(rest).Select(span => span.Text));
        }

        /// <summary>
        /// One pass of whisper over already-decoded audio, with the spans it reported kept.
        /// Their times are what tells a window that was read through from one that was not.
        /// </summary>
        private async Task<IReadOnlyList<HeardSpan>> ListenAsync(
            byte[] audio,
            string modelPath,
            string path,
            TimeSpan start,
            TimeSpan length,
            CancellationToken cancellationToken)
        {
            if (!await _gate.WaitAsync(SlotWait, cancellationToken))
            {
                throw new TranscriptionUnavailableException(
                    $"No transcription slot came free within {SlotWait.TotalMinutes:F0} minutes; another run is not finishing.");
            }

            try
            {
                var factory = GetFactory(modelPath);
                using var processor = factory.CreateBuilder()
                    .WithLanguage("en")
                    .WithThreads(TranscriptionParallelism.ThreadsPerSlot)
                    .Build();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(RunTimeout);

                using var stream = new MemoryStream(audio, writable: false);
                var spans = new List<HeardSpan>();
                try
                {
                    await foreach (var segment in processor.ProcessAsync(stream, timeout.Token))
                    {
                        var text = segment.Text.Trim();
                        if (text.Length > 0)
                        {
                            spans.Add(new HeardSpan(segment.Start, segment.End, text));
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TranscriptionTimedOutException(
                        $"Listening to {length.TotalSeconds:F0}s of {LogRedaction.SanitizeFilePath(path)} at {start:c} took longer than {RunTimeout.TotalMinutes:F0} minutes.");
                }

                return spans;
            }
            finally
            {
                _gate.Release();
            }
        }

        private static int Words(IEnumerable<HeardSpan> spans) =>
            spans.Sum(span => span.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
