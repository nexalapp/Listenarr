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
using System.Text;
using Listenarr.Domain.Audiobooks.Conversion;

namespace Listenarr.Infrastructure.Ffmpeg.Conversion
{
    /// <summary>
    /// Builds the ffmpeg argument list and chapter metadata for one conversion.
    /// Separated from process execution so the command can be asserted in tests
    /// without an encoder present.
    /// </summary>
    internal static class FfmpegCommandBuilder
    {
        /// <summary>
        /// Render the FFMETADATA document carrying the tags and chapter marks.
        ///
        /// The description goes in as <c>description</c>, which ffmpeg's mov muxer writes
        /// to the MP4 <c>desc</c> atom. That atom is the whole point: Plex populates an
        /// album summary from it and from nothing else.
        /// </summary>
        public static string BuildMetadataDocument(ConversionPlan plan, AudioMetadata tags)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(tags);

            var builder = new StringBuilder();
            builder.Append(";FFMETADATA1\n");

            AppendTag(builder, "title", tags.Album is { Length: > 0 } ? tags.Album : tags.Title);
            AppendTag(builder, "album", tags.Album is { Length: > 0 } ? tags.Album : tags.Title);
            AppendTag(builder, "artist", tags.Artist);
            AppendTag(builder, "album_artist", tags.AlbumArtist is { Length: > 0 } ? tags.AlbumArtist : tags.Artist);
            AppendTag(builder, "composer", tags.Narrator);
            AppendTag(builder, "genre", tags.Genre is { Length: > 0 } ? tags.Genre : "Audiobook");
            AppendTag(builder, "description", tags.Description);
            AppendTag(builder, "publisher", tags.Publisher);
            AppendTag(builder, "language", tags.Language);

            if (tags.Year is > 0)
            {
                AppendTag(builder, "date", tags.Year.Value.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var chapter in plan.Chapters)
            {
                builder.Append("[CHAPTER]\n");
                builder.Append("TIMEBASE=1/1000\n");
                builder.Append(CultureInfo.InvariantCulture, $"START={(long)chapter.Start.TotalMilliseconds}\n");
                builder.Append(CultureInfo.InvariantCulture, $"END={(long)chapter.End.TotalMilliseconds}\n");
                AppendTag(builder, "title", chapter.Title);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Build the argument list.
        ///
        /// Every source is a separate <c>-i</c> joined by the concat <i>filter</i>, not the
        /// concat <i>demuxer</i>. The demuxer silently adopts the first input's sample rate
        /// and channel layout for the whole book — a 44.1kHz stereo chapter following a
        /// 22kHz mono one gets downmixed with no error — and its output drifts from the
        /// nominal duration, which walks the chapter marks out of sync over a long book.
        /// The filter resamples each input to one declared target and concatenates
        /// sample-accurately.
        /// </summary>
        public static IReadOnlyList<string> BuildArguments(
            ConversionPlan plan,
            string metadataPath,
            string outputPath,
            string? coverArtPath)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            if (plan.OrderedSources.Count == 0)
            {
                throw new ArgumentException("A conversion needs at least one source file.", nameof(plan));
            }

            var args = new List<string>
            {
                "-hide_banner",
                "-nostdin",
                "-y",
                // Machine-readable progress on stdout keeps the human-readable log on
                // stderr usable as a failure diagnostic.
                "-progress", "pipe:1",
                "-nostats",
                "-v", "warning"
            };

            foreach (var source in plan.OrderedSources)
            {
                args.Add("-i");
                args.Add(source.FullPath);
            }

            var metadataIndex = plan.OrderedSources.Count;
            args.Add("-i");
            args.Add(metadataPath);

            var coverIndex = -1;
            if (!string.IsNullOrWhiteSpace(coverArtPath))
            {
                coverIndex = metadataIndex + 1;
                args.Add("-i");
                args.Add(coverArtPath);
            }

            args.Add("-filter_complex");
            args.Add(BuildFilterGraph(plan));

            args.Add("-map");
            args.Add("[out]");

            if (coverIndex >= 0)
            {
                args.Add("-map");
                args.Add($"{coverIndex}:v");
                args.Add("-c:v");
                args.Add("copy");
                args.Add("-disposition:v");
                args.Add("attached_pic");
            }

            args.Add("-map_metadata");
            args.Add(metadataIndex.ToString(CultureInfo.InvariantCulture));

            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add(plan.TargetBitRate.ToString(CultureInfo.InvariantCulture));
            args.Add("-ar");
            args.Add(plan.TargetSampleRate.ToString(CultureInfo.InvariantCulture));
            args.Add("-ac");
            args.Add(plan.TargetChannels.ToString(CultureInfo.InvariantCulture));

            // The ipod muxer is what makes this an audiobook rather than a video file
            // with an audio track: it writes the M4B-shaped brand players look for.
            args.Add("-f");
            args.Add("ipod");
            args.Add(outputPath);

            return args;
        }

        /// <summary>
        /// One <c>aformat</c> per input normalising it to the plan's target, then a single
        /// concat. Declaring the format per input is what stops the graph from inheriting
        /// the first input's parameters.
        /// </summary>
        private static string BuildFilterGraph(ConversionPlan plan)
        {
            var layout = plan.TargetChannels >= 2 ? "stereo" : "mono";
            var graph = new StringBuilder();

            for (var i = 0; i < plan.OrderedSources.Count; i++)
            {
                graph.Append(CultureInfo.InvariantCulture,
                    $"[{i}:a]aformat=sample_fmts=fltp:sample_rates={plan.TargetSampleRate}:channel_layouts={layout}[a{i}];");
            }

            for (var i = 0; i < plan.OrderedSources.Count; i++)
            {
                graph.Append(CultureInfo.InvariantCulture, $"[a{i}]");
            }

            graph.Append(CultureInfo.InvariantCulture,
                $"concat=n={plan.OrderedSources.Count}:v=0:a=1[out]");

            return graph.ToString();
        }

        /// <summary>
        /// FFMETADATA gives <c>=</c>, <c>;</c>, <c>#</c>, <c>\</c> and newline special meaning,
        /// so a chapter title or description carrying any of them has to escape it or the
        /// document stops parsing where the character appears.
        /// </summary>
        private static void AppendTag(StringBuilder builder, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            builder.Append(key).Append('=').Append(Escape(value)).Append('\n');
        }

        private static string Escape(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '=':
                    case ';':
                    case '#':
                    case '\\':
                        builder.Append('\\').Append(c);
                        break;
                    case '\r':
                        break;
                    case '\n':
                        // A literal newline would end the value and could inject a
                        // key or a [CHAPTER] header from tag text.
                        builder.Append("\\\n");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
