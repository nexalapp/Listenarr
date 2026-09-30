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
namespace Listenarr.Application.Audiobooks.Transcription
{
    /// <summary>One stretch of a window and what was heard in it.</summary>
    public readonly record struct HeardSpan(TimeSpan Start, TimeSpan End, string Text);

    /// <summary>
    /// Finds where whisper stopped keeping up inside a window.
    ///
    /// <para>
    /// whisper reads a window in thirty-second chunks, and a chunk that opens on something
    /// it cannot make words of - a few seconds of music, a shop ident, a lone heading -
    /// can cost the rest of that chunk and sometimes the ones after it. It does not fail;
    /// it returns a shorter transcript, or the previous phrase again with a timestamp
    /// stretched over the part it dropped. Nothing downstream can tell that from a quiet
    /// passage.
    /// </para>
    /// <para>
    /// Measured on The Barsoom Project. Its closing ninety seconds carry the production
    /// credits behind twelve seconds of music: whisper gave up at 37.1s, returned "to life."
    /// - two words - stamped across sixteen seconds, and then nothing until the last three.
    /// Fed the same audio from 37.1s on its own it read every line of those credits. The
    /// book's opening does the same thing twice over, yielding "This is Audible." and
    /// "Prologue" for its first sixty seconds. So the remedy is to listen to the rest of the
    /// window again, and the job here is only to say where "the rest" starts.
    /// </para>
    /// </summary>
    public static class TranscriptStall
    {
        /// <summary>
        /// A silence this long between two spans is not a pause in the reading. Comfortably
        /// longer than the gap between chapters of an audiobook and comfortably shorter than
        /// the thirty-second chunk a stall costs.
        /// </summary>
        private static readonly TimeSpan Unaccounted = TimeSpan.FromSeconds(10);

        /// <summary>
        /// A span saying this little for its length is the previous phrase stretched over
        /// dropped audio rather than speech. Ordinary narration runs above two words a
        /// second, so this is generous by four times.
        /// </summary>
        private const double WordsPerSecond = 0.5;

        /// <summary>Short spans are not judged on their rate; "Prologue" is a real heading.</summary>
        private static readonly TimeSpan LongEnoughToJudge = TimeSpan.FromSeconds(8);

        /// <summary>Listening again has to be worth a decode.</summary>
        private static readonly TimeSpan WorthHearingAgain = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Where to start listening again, or null when the window was heard through.
        /// </summary>
        /// <param name="spans">What came back, in order.</param>
        /// <param name="window">How long the window was.</param>
        public static TimeSpan? FirstStall(IReadOnlyList<HeardSpan> spans, TimeSpan window)
        {
            if (spans.Count == 0)
            {
                // Nothing at all came back. There is no evidence a second listen would do
                // better, and a window of silence would pay for a decode every time.
                return null;
            }

            // A gap before the first span is the recording's own lead-in, not a stall, and
            // listening again from zero would only repeat the window.
            var heardTo = spans[0].End;
            for (var i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                if (i > 0 && span.Start - heardTo >= Unaccounted)
                {
                    return Worth(heardTo, window);
                }

                if (Stalled(span))
                {
                    return Worth(span.Start, window);
                }

                if (span.End > heardTo)
                {
                    heardTo = span.End;
                }
            }

            return window - heardTo >= Unaccounted ? Worth(heardTo, window) : null;
        }

        private static bool Stalled(HeardSpan span)
        {
            var length = span.End - span.Start;
            if (length < LongEnoughToJudge)
            {
                return false;
            }

            var words = span.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            return words / length.TotalSeconds < WordsPerSecond;
        }

        private static TimeSpan? Worth(TimeSpan from, TimeSpan window) =>
            window - from >= WorthHearingAgain ? from : null;
    }
}
