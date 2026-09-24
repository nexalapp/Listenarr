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
using System.Text.RegularExpressions;

namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>How a recording finishes.</summary>
    public enum AudioEnding
    {
        /// <summary>Nothing was heard at the end, so nothing can be said.</summary>
        Unknown,

        /// <summary>A publisher's closing credits: this recording finished.</summary>
        Credits,

        /// <summary>The prose stops on a whole sentence. Probably finished, not provably.</summary>
        CompleteSentence,

        /// <summary>The prose stops in the middle of a clause. The file is cut off.</summary>
        MidClause
    }

    /// <summary>
    /// Whether a book's audio ends, or merely stops.
    ///
    /// <para>
    /// This replaces judging completeness by runtime, which could not work: a recording
    /// shorter than the record is usually a different edition rather than a damaged file,
    /// and across this library that framing was wrong about far more books than it was
    /// right about. How the audio ends answers the question directly. A finished
    /// production reads its credits; a truncated one stops mid-sentence, sometimes
    /// mid-word - "and even more upriver," and "I don't," he admitted. "I flew o".
    /// </para>
    /// <para>
    /// Measured over 1,223 transcripts: 1,075 end in credits, 133 on a whole sentence and
    /// 3 mid-clause. Every one of those three also had a large runtime gap, which is the
    /// corroboration that the rule is finding real damage rather than transcription noise.
    /// </para>
    /// </summary>
    public static partial class AudioEndings
    {
        /// <summary>
        /// A trailing fragment shorter than this is the transcriber tailing off into
        /// silence, not evidence of a severed file. Single words - "you", "real",
        /// "impatient" - were the bulk of the false positives.
        /// </summary>
        private const int ShortestMeaningfulTail = 4;

        [GeneratedRegex(@"^[\[\(][^\]\)]*[\]\)]$")]
        private static partial Regex SoundOnly();

        [GeneratedRegex(@"[.!?""'\u201d\u2019\)]\s*$")]
        private static partial Regex Terminal();

        /// <summary>A credit rather than prose: "... by Bennett Cornwall", the last line of a rights notice.</summary>
        [GeneratedRegex(@"\bby\s+[A-Z][\w'.-]*(\s+[A-Z][\w'.-]*)*\s*$")]
        private static partial Regex TrailingCredit();

        private static readonly string[] CreditPhrases =
        [
            "copyright", "production", "produced by", "has been", "hope you have enjoyed",
            "hopes you have enjoyed", "all rights reserved", "audio presents", "the end",
            "end of", "narrated by", "performed by", "read by", "published by",
            "an imprint of", "www.", ".com", "visit "
        ];

        /// <summary>How the closing stretch of a book ends.</summary>
        public static AudioEnding Of(string? closing)
        {
            if (string.IsNullOrWhiteSpace(closing))
            {
                return AudioEnding.Unknown;
            }

            if (CreditPhrases.Any(phrase => closing.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
            {
                return AudioEnding.Credits;
            }

            // Whisper writes non-speech in brackets, and a book that ends in silence or
            // music ends on one of those rather than on its last words.
            var spoken = closing
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !SoundOnly().IsMatch(line))
                .ToList();
            if (spoken.Count == 0)
            {
                return AudioEnding.Unknown;
            }

            var last = spoken[^1];
            if (TrailingCredit().IsMatch(last))
            {
                return AudioEnding.Credits;
            }

            if (Terminal().IsMatch(last) || last.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < ShortestMeaningfulTail)
            {
                return AudioEnding.CompleteSentence;
            }

            return AudioEnding.MidClause;
        }

        /// <summary>The closing half of a stored transcript, or null when it holds only an opening.</summary>
        public static string? ClosingOf(string? transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return null;
            }

            var marker = transcript.IndexOf(AudioAuditTranscript.ClosingMarker, StringComparison.Ordinal);
            return marker < 0 ? null : transcript[(marker + AudioAuditTranscript.ClosingMarker.Length)..];
        }
    }
}
