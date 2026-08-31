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
using Listenarr.Infrastructure.Ffmpeg.Conversion;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Ffmpeg.Conversion
{
    [Trait("Name", "FfmpegCommandBuilderTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class FfmpegCommandBuilderTests : BaseTests
    {
        private static ConversionPlan Plan(
            int sourceCount = 2,
            int sampleRate = 44_100,
            int channels = 2,
            int bitRate = 64_000)
        {
            var sources = Enumerable.Range(1, sourceCount)
                .Select(i => new ConversionSource(
                    $"/library/book/Chapter {i}.mp3",
                    $"Chapter {i}.mp3",
                    TimeSpan.FromSeconds(60),
                    bitRate,
                    sampleRate,
                    channels))
                .ToList();

            return ConversionPlanner.BuildPlan(sources, StringComparer.Ordinal);
        }

        private static string ArgumentAfter(IReadOnlyList<string> args, string flag)
        {
            var index = args.ToList().IndexOf(flag);
            Assert.True(index >= 0 && index + 1 < args.Count, $"Expected a value after {flag}");
            return args[index + 1];
        }

        // ---- stream normalisation ---------------------------------------------------

        [Fact]
        public void BuildArguments_UsesTheConcatFilterRatherThanTheConcatDemuxer()
        {
            // The demuxer adopts the first input's parameters for the whole book and
            // drifts from the nominal duration. Neither is acceptable here.
            var args = FfmpegCommandBuilder.BuildArguments(Plan(), "/tmp/meta", "/tmp/out.m4b", null);

            Assert.DoesNotContain("concat", args.Where((a, i) => i > 0 && args[i - 1] == "-f"));
            Assert.Contains("-filter_complex", args);
            Assert.Contains("concat=n=2:v=0:a=1[out]", ArgumentAfter(args, "-filter_complex"));
        }

        [Fact]
        public void BuildArguments_NormalisesEveryInputToTheTargetParameters()
        {
            var args = FfmpegCommandBuilder.BuildArguments(Plan(sourceCount: 3), "/tmp/meta", "/tmp/out.m4b", null);
            var graph = ArgumentAfter(args, "-filter_complex");

            for (var i = 0; i < 3; i++)
            {
                Assert.Contains($"[{i}:a]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[a{i}];", graph);
            }
        }

        [Fact]
        public void BuildArguments_DeclaresMonoLayout_WhenThePlanIsSingleChannel()
        {
            var args = FfmpegCommandBuilder.BuildArguments(
                Plan(channels: 1),
                "/tmp/meta",
                "/tmp/out.m4b",
                null);

            Assert.Contains("channel_layouts=mono", ArgumentAfter(args, "-filter_complex"));
            Assert.Equal("1", ArgumentAfter(args, "-ac"));
        }

        [Fact]
        public void BuildArguments_PassesEachSourceAsItsOwnInput()
        {
            var args = FfmpegCommandBuilder.BuildArguments(Plan(sourceCount: 3), "/tmp/meta", "/tmp/out.m4b", null);

            Assert.Equal(4, args.Count(a => a == "-i")); // 3 sources plus the metadata document
            Assert.Contains("/library/book/Chapter 1.mp3", args);
            Assert.Contains("/library/book/Chapter 3.mp3", args);
        }

        [Fact]
        public void BuildArguments_TargetsTheM4bContainerAndAacEncoder()
        {
            var args = FfmpegCommandBuilder.BuildArguments(Plan(), "/tmp/meta", "/tmp/out.m4b", null);

            Assert.Equal("ipod", ArgumentAfter(args, "-f"));
            Assert.Equal("aac", ArgumentAfter(args, "-c:a"));
            Assert.Equal("64000", ArgumentAfter(args, "-b:a"));
            Assert.Equal("/tmp/out.m4b", args[^1]);
        }

        [Fact]
        public void BuildArguments_MapsCoverArtAsAttachedPicture_WhenSupplied()
        {
            var args = FfmpegCommandBuilder.BuildArguments(Plan(), "/tmp/meta", "/tmp/out.m4b", "/tmp/cover.jpg");

            Assert.Contains("/tmp/cover.jpg", args);
            Assert.Equal("attached_pic", ArgumentAfter(args, "-disposition:v"));
            Assert.Equal("copy", ArgumentAfter(args, "-c:v"));
        }

        [Fact]
        public void BuildArguments_OmitsVideoMapping_WhenNoCoverIsSupplied()
        {
            var args = FfmpegCommandBuilder.BuildArguments(Plan(), "/tmp/meta", "/tmp/out.m4b", null);

            Assert.DoesNotContain("-disposition:v", args);
            Assert.DoesNotContain("-c:v", args);
        }

        [Fact]
        public void BuildArguments_RequestsMachineReadableProgress()
        {
            var args = FfmpegCommandBuilder.BuildArguments(Plan(), "/tmp/meta", "/tmp/out.m4b", null);

            Assert.Equal("pipe:1", ArgumentAfter(args, "-progress"));
            Assert.Contains("-nostats", args);
        }

        // ---- metadata document ------------------------------------------------------

        [Fact]
        public void BuildMetadataDocument_WritesDescriptionForTheDescAtom()
        {
            // Plex reads an album summary from the MP4 desc atom and nowhere else, so
            // this key is the reason the whole conversion is worth doing.
            var document = FfmpegCommandBuilder.BuildMetadataDocument(
                Plan(),
                Tags(("description", "A hobbit leaves home.")));

            Assert.Contains("description=A hobbit leaves home.", document);
        }

        [Fact]
        public void BuildMetadataDocument_WritesEveryResolvedTagVerbatim()
        {
            // The tags arrive already resolved by the shared planner, so this builder
            // decides nothing about their values -- it only escapes and emits them. A
            // builder that reinterpreted them is how a converted book and an enriched one
            // would drift apart.
            var document = FfmpegCommandBuilder.BuildMetadataDocument(
                Plan(),
                Tags(
                    ("album", "[The Expanse 2.7] Drive"),
                    ("sort_album", "The Expanse 2.7 - Drive"),
                    ("SERIES", "The Expanse")));

            Assert.Contains("album=[The Expanse 2.7] Drive", document);
            Assert.Contains("sort_album=The Expanse 2.7 - Drive", document);
            Assert.Contains("SERIES=The Expanse", document);
        }

        [Fact]
        public void BuildMetadataDocument_WritesOneChapterBlockPerSource()
        {
            var document = FfmpegCommandBuilder.BuildMetadataDocument(Plan(sourceCount: 3), Tags());

            Assert.Equal(3, document.Split("[CHAPTER]").Length - 1);
            Assert.Contains("START=0\nEND=60000", document);
            Assert.Contains("START=60000\nEND=120000", document);
        }

        [Fact]
        public void BuildMetadataDocument_EscapesCharactersThatWouldEndAValue()
        {
            // An unescaped '=' or ';' silently truncates the tag; an unescaped newline
            // would let tag text introduce a key of its own.
            var document = FfmpegCommandBuilder.BuildMetadataDocument(
                Plan(),
                Tags(("description", "Chapter 1 = the start; see #2\nartist=Impostor")));

            Assert.Contains("\\=", document);
            Assert.Contains(@"\;", document);
            Assert.Contains("\\#", document);
            Assert.DoesNotContain("\nartist=Impostor", document);
        }

        [Fact]
        public void BuildMetadataDocument_OmitsTagsThatHaveNoValue()
        {
            // An empty value would write a key with nothing after it, which reads back as
            // a present-but-blank tag rather than an absent one.
            var document = FfmpegCommandBuilder.BuildMetadataDocument(
                Plan(),
                Tags(("description", ""), ("publisher", "   ")));

            Assert.DoesNotContain("description=", document);
            Assert.DoesNotContain("publisher=", document);
        }

        private static Dictionary<string, string> Tags(params (string Key, string Value)[] tags) =>
            tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.OrdinalIgnoreCase);
    }
}
