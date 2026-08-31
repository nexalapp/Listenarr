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

namespace Listenarr.Domain.Audiobooks.Conversion
{
    /// <summary>
    /// Turns a set of source files into the ordered chapter list and encode parameters
    /// for one M4B. Pure: no filesystem, no process, no clock.
    /// </summary>
    public static class ConversionPlanner
    {
        /// <summary>
        /// Ceiling on the output bitrate. Audiobook sources are spoken word and a
        /// transcode never recovers detail the MP3 encoder already discarded, so
        /// matching a source above this only spends space.
        /// </summary>
        public const int MaximumBitRate = 128_000;

        /// <summary>
        /// Floor on the output bitrate. A source whose bitrate could not be read, or
        /// which is implausibly low, still has to encode to something listenable.
        /// </summary>
        public const int MinimumBitRate = 32_000;

        private const int DefaultSampleRate = 44_100;
        private const int DefaultChannels = 2;

        /// <summary>A title that is only a number carries no more than the chapter index already does.</summary>
        private static readonly Regex TitleWithoutWords = new(@"^[\s\d\p{P}]*$", RegexOptions.Compiled);

        /// <summary>Leading track numbering in a filename, which the chapter number already expresses.</summary>
        private static readonly Regex LeadingTrackNumber = new(
            @"^\s*(?:track\s*)?\d+\s*[-_.\)\]]*\s*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Build the plan. Sources are ordered naturally (so "Chapter 10" follows
        /// "Chapter 9", not "Chapter 1"), chapter boundaries accumulate from the decoded
        /// durations, and the encode target is the strongest of the inputs under the cap.
        /// </summary>
        /// <param name="sources">Input files in any order.</param>
        /// <param name="pathComparer">Comparer matching the source filesystem's case semantics.</param>
        /// <exception cref="ArgumentException">No usable sources were supplied.</exception>
        public static ConversionPlan BuildPlan(
            IEnumerable<ConversionSource> sources,
            StringComparer pathComparer)
        {
            ArgumentNullException.ThrowIfNull(sources);
            ArgumentNullException.ThrowIfNull(pathComparer);

            var byPath = new Dictionary<string, ConversionSource>(pathComparer);
            foreach (var source in sources)
            {
                if (string.IsNullOrWhiteSpace(source.FullPath))
                {
                    continue;
                }

                // First occurrence wins, matching how the import planner de-duplicates.
                if (!byPath.ContainsKey(source.FullPath))
                {
                    byPath[source.FullPath] = source;
                }
            }

            if (byPath.Count == 0)
            {
                throw new ArgumentException("A conversion needs at least one source file.", nameof(sources));
            }

            // Reuse the import path's ordering rather than sorting here. Plain filename
            // sort puts "Chapter 10" before "Chapter 2", and the two entry points must
            // agree on order or a re-import would renumber the chapters.
            var plans = MultiFileImportPlanner.BuildPlans(
                byPath.Values.Select(s => (s.FullPath, s.RelativePath)),
                pathComparer);

            var ordered = plans
                .OrderBy(p => p.SequenceNumber)
                .Select(p => byPath[p.FullPath])
                .ToList();

            // An embedded title only names a chapter if it tells the files apart. Parts
            // split from one book commonly all carry the book's own title tag, and using
            // it would name every chapter identically — worse than the filenames, which
            // at least differ.
            var repeatedTitles = ordered
                .Select(source => source.EmbeddedTitle?.Trim())
                .Where(title => !string.IsNullOrEmpty(title))
                .GroupBy(title => title, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var chapters = new List<ConversionChapter>(ordered.Count);
            var cursor = TimeSpan.Zero;
            foreach (var source in ordered)
            {
                // A non-positive duration would collapse the chapter onto its neighbour
                // and make the mark unusable, so clamp it to zero and let it carry the
                // start offset forward without moving it.
                var duration = source.Duration > TimeSpan.Zero ? source.Duration : TimeSpan.Zero;

                // A file that already carries chapter marks keeps them. A book previously
                // merged into one chaptered MP3 is the case that matters: treating it as
                // one chapter would discard every mark it has, and the merge is exactly
                // why someone would convert it.
                var embedded = NormaliseEmbeddedChapters(source, duration);
                if (embedded.Count > 0)
                {
                    foreach (var chapter in embedded)
                    {
                        chapters.Add(new ConversionChapter(
                            chapters.Count + 1,
                            !string.IsNullOrWhiteSpace(chapter.Title)
                                && !TitleWithoutWords.IsMatch(chapter.Title)
                                    ? chapter.Title.Trim()
                                    : $"Chapter {chapters.Count + 1}",
                            cursor + chapter.Start,
                            cursor + chapter.End,
                            source.FullPath));
                    }
                }
                else
                {
                    chapters.Add(new ConversionChapter(
                        chapters.Count + 1,
                        BuildChapterTitle(source, chapters.Count + 1, repeatedTitles),
                        cursor,
                        cursor + duration,
                        source.FullPath));
                }

                cursor += duration;
            }

            return new ConversionPlan(
                ordered,
                chapters,
                SelectBitRate(ordered),
                SelectSampleRate(ordered),
                SelectChannels(ordered));
        }

        /// <summary>
        /// Highest source bitrate, held between the floor and the cap. Taking the highest
        /// rather than the first matters: a book whose opening file is a low-bitrate
        /// preamble would otherwise transcode the whole book down to it.
        /// </summary>
        private static int SelectBitRate(IReadOnlyList<ConversionSource> sources)
        {
            var best = sources
                .Select(s => s.BitRate)
                .Where(b => b is > 0)
                .Select(b => b!.Value)
                .DefaultIfEmpty(0)
                .Max();

            if (best <= 0)
            {
                return MinimumBitRate;
            }

            return Math.Clamp(best, MinimumBitRate, MaximumBitRate);
        }

        /// <summary>
        /// Highest source sample rate. Every input is resampled to this one target, so
        /// choosing the maximum avoids discarding bandwidth the better files still have.
        /// </summary>
        private static int SelectSampleRate(IReadOnlyList<ConversionSource> sources)
        {
            var best = sources
                .Select(s => s.SampleRate)
                .Where(r => r is > 0)
                .Select(r => r!.Value)
                .DefaultIfEmpty(0)
                .Max();

            return best > 0 ? best : DefaultSampleRate;
        }

        /// <summary>
        /// Highest source channel count, capped at stereo. Mixing a mono file up to
        /// stereo is lossless; folding a stereo file down to mono is not.
        /// </summary>
        private static int SelectChannels(IReadOnlyList<ConversionSource> sources)
        {
            var best = sources
                .Select(s => s.Channels)
                .Where(c => c is > 0)
                .Select(c => c!.Value)
                .DefaultIfEmpty(0)
                .Max();

            if (best <= 0)
            {
                return DefaultChannels;
            }

            return Math.Min(best, 2);
        }

        /// <summary>
        /// Put a file's own chapter marks into usable shape: ordered, inside the file,
        /// and non-empty.
        ///
        /// These come from whatever tool wrote the file, so they cannot be trusted to be
        /// sorted, to stay within the audio, or to have sensible ends. A mark outside the
        /// file would land in a neighbouring chapter's audio once offset.
        /// </summary>
        private static List<EmbeddedChapter> NormaliseEmbeddedChapters(
            ConversionSource source,
            TimeSpan duration)
        {
            var embedded = source.EmbeddedChapters;
            if (embedded == null || embedded.Count == 0 || duration <= TimeSpan.Zero)
            {
                return [];
            }

            var results = new List<EmbeddedChapter>(embedded.Count);
            foreach (var chapter in embedded.OrderBy(c => c.Start))
            {
                var start = Clamp(chapter.Start, TimeSpan.Zero, duration);
                var end = Clamp(chapter.End, start, duration);
                if (end <= start)
                {
                    continue;
                }

                results.Add(new EmbeddedChapter(chapter.Title, start, end));
            }

            // A single mark spanning the file says no more than one chapter per file
            // already does, so fall back and let the filename supply a better title.
            if (results.Count == 1 && results[0].Start == TimeSpan.Zero && results[0].End >= duration)
            {
                return [];
            }

            return results;
        }

        private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
            value < min ? min : value > max ? max : value;

        /// <summary>
        /// Prefer the embedded title, fall back to the filename, and fall back again to
        /// the chapter number. A title that is only digits and punctuation is discarded:
        /// it says no more than the number already does.
        /// </summary>
        private static string BuildChapterTitle(
            ConversionSource source,
            int number,
            IReadOnlySet<string> repeatedTitles)
        {
            var embedded = source.EmbeddedTitle?.Trim();
            if (!string.IsNullOrEmpty(embedded)
                && !TitleWithoutWords.IsMatch(embedded)
                && !repeatedTitles.Contains(embedded))
            {
                return embedded;
            }

            var stem = Path.GetFileNameWithoutExtension(source.FullPath) ?? string.Empty;
            stem = LeadingTrackNumber.Replace(stem, string.Empty).Trim();
            stem = stem.Replace('_', ' ').Trim();

            if (!string.IsNullOrEmpty(stem) && !TitleWithoutWords.IsMatch(stem))
            {
                return stem;
            }

            return $"Chapter {number}";
        }
    }
}
