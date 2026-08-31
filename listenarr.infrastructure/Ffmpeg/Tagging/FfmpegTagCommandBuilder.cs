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

namespace Listenarr.Infrastructure.Ffmpeg.Tagging
{
    /// <summary>
    /// Builds the ffmpeg argument list for one tag rewrite. Separated from process
    /// execution so the command can be asserted in tests without an encoder present.
    /// </summary>
    internal static class FfmpegTagCommandBuilder
    {
        /// <summary>
        /// Rewrite a container with a new metadata set and nothing else changed.
        ///
        /// <para>
        /// <c>-c copy</c> throughout: the audio is not touched. A 600MB book rewritten to
        /// carry a different blurb must come back bit-identical in its samples, and
        /// re-encoding it would be slow, lossy, and pointless.
        /// </para>
        /// <para>
        /// <c>-map_metadata -1</c> drops the input's global metadata before the explicit
        /// <c>-metadata</c> arguments put the whole intended set back. Merging instead
        /// would leave the duplicate <c>SERIES</c> atoms several files in this library
        /// already carry, and would add another on every run; replacing wholesale is what
        /// makes a second run produce the same file rather than a longer one.
        /// </para>
        /// <para>
        /// Streams are mapped one at a time rather than with <c>-map 0</c>. An M4B's
        /// chapter marks live in a text stream that <c>-map_chapters</c> regenerates, so
        /// carrying the old one across would leave the file with two.
        /// </para>
        /// </summary>
        /// <param name="sourcePath">The file to rewrite. Never written to.</param>
        /// <param name="outputPath">Scratch path for the rewritten copy.</param>
        /// <param name="tags">The complete tag set the output should carry.</param>
        /// <param name="sourceHasCoverArt">Whether the source carries an attached picture worth keeping.</param>
        /// <param name="coverArtPath">A replacement cover, or null to keep the source's own.</param>
        public static IReadOnlyList<string> BuildArguments(
            string sourcePath,
            string outputPath,
            IReadOnlyDictionary<string, string> tags,
            bool sourceHasCoverArt,
            string? coverArtPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentNullException.ThrowIfNull(tags);

            var args = new List<string>
            {
                "-hide_banner",
                "-nostdin",
                "-y",
                // Machine-readable progress on stdout keeps the human-readable log on
                // stderr usable as a failure diagnostic.
                "-progress", "pipe:1",
                "-nostats",
                "-v", "warning",
                "-i", sourcePath
            };

            var replacingCover = !string.IsNullOrWhiteSpace(coverArtPath);
            if (replacingCover)
            {
                args.Add("-i");
                args.Add(coverArtPath!);
            }

            // Audio first, so the audio stream stays stream 0 in the output. A player that
            // opens the first stream must find the book, not the cover.
            args.Add("-map");
            args.Add("0:a");

            var hasPicture = replacingCover || sourceHasCoverArt;
            if (replacingCover)
            {
                args.Add("-map");
                args.Add("1:v");
            }
            else if (sourceHasCoverArt)
            {
                // "?" so a source whose picture stream turns out not to be mappable does
                // not fail the whole rewrite over its cover.
                args.Add("-map");
                args.Add("0:v?");
            }

            args.Add("-c");
            args.Add("copy");

            if (hasPicture)
            {
                // Without this the picture is written as a video stream, and players show
                // the book as a video file with one very long frame.
                args.Add("-disposition:v:0");
                args.Add("attached_pic");
            }

            // Explicit rather than relying on the default: ffmpeg's default chapter
            // mapping picks the input with the most chapters, which happens to be right
            // here but is not a promise, and a book that silently lost its chapters would
            // look like a successful rewrite.
            args.Add("-map_chapters");
            args.Add("0");

            args.Add("-map_metadata");
            args.Add("-1");

            foreach (var (key, value) in tags)
            {
                if (string.IsNullOrWhiteSpace(key) || value == null)
                {
                    continue;
                }

                args.Add("-metadata");
                args.Add($"{key}={value}");
            }

            // The moov atom goes to the front, so a player can start the book without
            // reading to the end of the file first. The source may or may not have been
            // written that way; the rewrite is the chance to make sure.
            args.Add("-movflags");
            args.Add("+faststart");

            // The ipod muxer is what keeps this an audiobook rather than a video file
            // with an audio track: it writes the M4B-shaped brand players look for.
            args.Add("-f");
            args.Add("ipod");
            args.Add(outputPath);

            return args;
        }
    }
}
