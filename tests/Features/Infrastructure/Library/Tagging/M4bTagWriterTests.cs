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
using System.Diagnostics;
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Infrastructure.Ffmpeg.Tagging;
using Listenarr.Infrastructure.Library.Tagging;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Tagging
{
    /// <summary>
    /// Exercises the real encoder against real M4B files.
    ///
    /// These run wherever ffmpeg and ffprobe are on PATH — the Docker dev environment
    /// installs both — and are skipped elsewhere, because a host without an encoder
    /// proves nothing either way. They are the only evidence that the atoms actually
    /// land: writing without verifying is how this silently does nothing.
    /// </summary>
    [Trait("Name", "M4bTagWriterTests")]
    [Trait("Category", "Tagging")]
    public sealed class M4bTagWriterTests : BaseTests, IDisposable
    {
        private readonly string _workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "listenarr-tagging-" + Guid.NewGuid().ToString("N"));

        private static string? FindOnPath(string binary) => EncoderFactAttribute.FindOnPath(binary);

        private static M4bTagWriter BuildWriter(string? ffprobePath = null, bool useDefaultProbe = true) =>
            new(
                new FfprobeTagReader(
                    new PathResolvedFfmpegService(
                        FindOnPath("ffmpeg"),
                        useDefaultProbe ? FindOnPath("ffprobe") : ffprobePath),
                    new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance)),
                NullLogger<M4bTagWriter>.Instance);

        private string OutputPath => Path.Combine(_workingDirectory, "out.m4b");

        private static Dictionary<string, string> Tags(params (string Key, string Value)[] tags) =>
            tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.OrdinalIgnoreCase);

        private async Task RunFfmpegAsync(params string[] arguments)
        {
            Directory.CreateDirectory(_workingDirectory);
            var startInfo = new ProcessStartInfo
            {
                FileName = FindOnPath("ffmpeg")!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }

            var runner = new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance);
            var result = await runner.RunAsync(startInfo, 60_000);
            Assert.True(result.ExitCode == 0, result.Stderr);
        }

        /// <summary>A real M4B, optionally chaptered, optionally with cover art and starting tags.</summary>
        private async Task<string> WriteBookAsync(
            string name = "book.m4b",
            int seconds = 4,
            int chapters = 0,
            bool coverArt = false,
            string? extraMetadataDocument = null)
        {
            Directory.CreateDirectory(_workingDirectory);
            var path = Path.Combine(_workingDirectory, name);

            var arguments = new List<string>
            {
                "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}"
            };

            if (coverArt)
            {
                arguments.AddRange(["-f", "lavfi", "-i", "color=c=red:s=64x64:d=1"]);
            }

            string? metadataPath = null;
            if (chapters > 0 || extraMetadataDocument != null)
            {
                metadataPath = Path.Combine(_workingDirectory, name + ".ffmetadata");
                var builder = new System.Text.StringBuilder(";FFMETADATA1\n");
                if (extraMetadataDocument != null)
                {
                    builder.Append(extraMetadataDocument);
                }

                var slice = seconds * 1000 / Math.Max(chapters, 1);
                for (var i = 0; i < chapters; i++)
                {
                    builder.Append("[CHAPTER]\nTIMEBASE=1/1000\n");
                    builder.Append($"START={i * slice}\nEND={(i + 1) * slice}\n");
                    builder.Append($"title=Chapter {i + 1}\n");
                }

                await File.WriteAllTextAsync(metadataPath, builder.ToString());
                arguments.AddRange(["-i", metadataPath]);
            }

            arguments.AddRange(["-map", "0:a"]);
            if (coverArt)
            {
                arguments.AddRange(["-map", "1:v", "-c:v", "mjpeg", "-frames:v", "1", "-disposition:v", "attached_pic"]);
            }

            if (metadataPath != null)
            {
                arguments.AddRange(["-map_metadata", coverArt ? "2" : "1"]);
            }

            arguments.AddRange(["-c:a", "aac", "-b:a", "64k", "-f", "ipod", path]);

            await RunFfmpegAsync([.. arguments]);
            return path;
        }

        // ---- the atom that matters ---------------------------------------------------

        [EncoderFact]
        public async Task WriteAsync_PutsTheDescriptionInTheMp4DescAtom()
        {
            // Plex reads an album summary from the desc atom and from nothing else. This
            // is the acceptance condition the fork was created for, asserted against the
            // bytes rather than ffprobe's normalised view of them: ffmpeg could satisfy
            // ffprobe by writing the value somewhere Plex will never look.
            var source = await WriteBookAsync();
            var writer = BuildWriter();
            var existing = await writer.ReadAsync(source);

            var result = await writer.WriteAsync(new TagWriteRequest(
                source,
                OutputPath,
                Tags(("description", "A gripping military space opera.")),
                existing));

            Assert.True(result.Success, result.Message);

            var bytes = await File.ReadAllBytesAsync(OutputPath);
            var descAtom = IndexOf(bytes, "desc"u8);
            Assert.True(descAtom >= 0, "The output carries no desc atom.");

            var value = IndexOf(bytes, "A gripping military space opera."u8);
            Assert.True(value > descAtom, "The description is not stored in the desc atom.");
            Assert.True(
                value - descAtom < 64,
                "The description is too far from the desc atom header to be its payload.");
        }

        [EncoderFact]
        public async Task WriteAsync_WritesTheLibrarysBracketedAlbumAndItsSortableTwin()
        {
            var source = await WriteBookAsync();
            var writer = BuildWriter();
            var existing = await writer.ReadAsync(source);

            var result = await writer.WriteAsync(new TagWriteRequest(
                source,
                OutputPath,
                Tags(
                    ("album", "[The Expanse 2.7] Drive"),
                    ("sort_album", "The Expanse 2.7 - Drive"),
                    ("composer", "Jefferson Mays"),
                    ("SERIES", "The Expanse"),
                    ("SERIESPOSITION", "2.7"),
                    ("ASIN", "B00A2M2XPO")),
                existing));

            Assert.True(result.Success, result.Message);

            var written = await writer.ReadAsync(OutputPath);
            Assert.Equal("[The Expanse 2.7] Drive", written.Tags["album"]);
            Assert.Equal("The Expanse 2.7 - Drive", written.Tags["sort_album"]);
            Assert.Equal("Jefferson Mays", written.Tags["composer"]);
            // Freeform atoms: MP4 has no standard key for any of these, and this is the
            // shape the library's existing files already carry.
            Assert.Equal("The Expanse", written.Tags["SERIES"]);
            Assert.Equal("2.7", written.Tags["SERIESPOSITION"]);
            Assert.Equal("B00A2M2XPO", written.Tags["ASIN"]);

            // And they are iTunes "----" freeform atoms, not QuickTime mdta entries.
            // ffprobe reads both back under the same names, so the read alone cannot tell
            // them apart -- but players read the former and ignore the latter.
            var bytes = await File.ReadAllBytesAsync(OutputPath);
            var mean = IndexOf(bytes, "com.apple.iTunes"u8);
            Assert.True(mean >= 0, "The freeform tags were not written as iTunes atoms.");
            Assert.True(
                IndexOf(bytes, "keys"u8) < 0 || mean >= 0,
                "The file carries a QuickTime keys table rather than iTunes atoms.");
        }

        // ---- what must survive the rewrite -------------------------------------------

        [EncoderFact]
        public async Task WriteAsync_KeepsChapters()
        {
            // ffmpeg drops chapters silently when they are not mapped, and a book that
            // lost them is a failure however happy ffmpeg was.
            var source = await WriteBookAsync(chapters: 4, seconds: 8);
            var writer = BuildWriter();
            var existing = await writer.ReadAsync(source);
            Assert.Equal(4, existing.ChapterCount);

            var result = await writer.WriteAsync(new TagWriteRequest(
                source,
                OutputPath,
                Tags(("title", "Drive")),
                existing));

            Assert.True(result.Success, result.Message);
            Assert.Equal(4, (await writer.ReadAsync(OutputPath)).ChapterCount);
        }

        [EncoderFact]
        public async Task WriteAsync_KeepsCoverArtWhileWritingFreeformTags()
        {
            // The combination that ruled ffmpeg out. Its mov muxer will write freeform
            // atoms (-movflags +use_metadata_tags) or an attached picture, never both:
            // with the flag on it emits no covr atom at all. A real library file has
            // cover art and needs SERIES, so a writer that cannot do both is no use.
            var source = await WriteBookAsync(coverArt: true, chapters: 3, seconds: 6);
            var writer = BuildWriter();
            var existing = await writer.ReadAsync(source);
            Assert.True(existing.HasCoverArt);

            var result = await writer.WriteAsync(new TagWriteRequest(
                source,
                OutputPath,
                Tags(
                    ("title", "Drive"),
                    ("album", "[The Expanse 2.7] Drive"),
                    ("description", "A short story of the Expanse."),
                    ("SERIES", "The Expanse"),
                    ("ASIN", "B00A2M2XPO")),
                existing));

            Assert.True(result.Success, result.Message);

            var written = await writer.ReadAsync(OutputPath);

            // Not merely "has a video stream": without the attached_pic disposition a
            // player shows the book as a video file with one very long frame.
            Assert.True(written.HasCoverArt);
            Assert.Equal(3, written.ChapterCount);
            Assert.Equal("The Expanse", written.Tags["SERIES"]);
            Assert.Equal("B00A2M2XPO", written.Tags["ASIN"]);
            Assert.Equal("[The Expanse 2.7] Drive", written.Tags["album"]);

            // And the description is in the desc atom, which is the only place Plex
            // reads an album summary from.
            var bytes = await File.ReadAllBytesAsync(OutputPath);
            var descAtom = IndexOf(bytes, "desc"u8);
            var value = IndexOf(bytes, "A short story of the Expanse."u8);
            Assert.True(descAtom >= 0 && value > descAtom && value - descAtom < 64);
        }

        [EncoderFact]
        public async Task WriteAsync_KeepsTheAudioUntouched()
        {
            var source = await WriteBookAsync(seconds: 6);
            var writer = BuildWriter();
            var existing = await writer.ReadAsync(source);

            var result = await writer.WriteAsync(new TagWriteRequest(
                source,
                OutputPath,
                Tags(("title", "Drive")),
                existing));

            Assert.True(result.Success, result.Message);

            var written = await writer.ReadAsync(OutputPath);
            Assert.True(
                (written.Duration - existing.Duration).Duration() < TimeSpan.FromSeconds(1),
                $"Duration changed from {existing.Duration} to {written.Duration}.");
        }

        // ---- re-runnable --------------------------------------------------------------

        [EncoderFact]
        public async Task WriteAsync_RunTwice_ProducesTheSameTagsRatherThanTwoCopies()
        {
            // "Re-runnable without accumulating" is a real requirement: several files in
            // this library already carry a duplicate SERIES atom from being tagged twice
            // by something else.
            var source = await WriteBookAsync();
            var writer = BuildWriter();

            // A value that appears in no other tag, so counting it in the bytes counts
            // atoms rather than substrings.
            var tags = Tags(("SERIES", "Paragon-Space-Marker"), ("album", "Starship Raider"));

            var first = await writer.WriteAsync(new TagWriteRequest(
                source,
                OutputPath,
                tags,
                await writer.ReadAsync(source)));
            Assert.True(first.Success, first.Message);

            var secondOutput = Path.Combine(_workingDirectory, "out2.m4b");
            var second = await writer.WriteAsync(new TagWriteRequest(
                OutputPath,
                secondOutput,
                tags,
                await writer.ReadAsync(OutputPath)));
            Assert.True(second.Success, second.Message);

            var written = await writer.ReadAsync(secondOutput);
            Assert.Equal("Paragon-Space-Marker", written.Tags["SERIES"]);

            // One atom, not two: setting a tag replaces the atoms already carrying it,
            // so a second pass cannot append a second copy.
            var bytes = await File.ReadAllBytesAsync(secondOutput);
            Assert.Equal(1, CountOccurrences(bytes, "Paragon-Space-Marker"u8));
        }

        [EncoderFact]
        public async Task WriteAsync_ReplacesAnExistingValueRatherThanAddingToIt()
        {
            // The mechanism behind "re-runnable without accumulating": setting a tag
            // removes the atoms already carrying it. Several files in this library carry
            // a duplicate SERIES from being tagged twice by something that appended.
            var source = await WriteBookAsync();
            var writer = BuildWriter();

            var first = await writer.WriteAsync(new TagWriteRequest(
                source,
                OutputPath,
                Tags(("SERIES", "Old-Series-Marker")),
                await writer.ReadAsync(source)));
            Assert.True(first.Success, first.Message);

            var secondOutput = Path.Combine(_workingDirectory, "replaced.m4b");
            var second = await writer.WriteAsync(new TagWriteRequest(
                OutputPath,
                secondOutput,
                Tags(("SERIES", "New-Series-Marker")),
                await writer.ReadAsync(OutputPath)));
            Assert.True(second.Success, second.Message);

            var written = await writer.ReadAsync(secondOutput);
            Assert.Equal("New-Series-Marker", written.Tags["SERIES"]);

            var bytes = await File.ReadAllBytesAsync(secondOutput);
            Assert.Equal(1, CountOccurrences(bytes, "New-Series-Marker"u8));
            Assert.Equal(0, CountOccurrences(bytes, "Old-Series-Marker"u8));
        }

        [EncoderFact]
        public async Task ApplyAsync_TagsAFileInPlaceWithoutCopyingIt()
        {
            // The conversion path: its output is already scratch, and copying several
            // hundred megabytes again to protect a file nothing is serving would be pure
            // cost.
            var source = await WriteBookAsync(chapters: 2, seconds: 4);
            var writer = BuildWriter();

            var result = await writer.ApplyAsync(
                source,
                Tags(("SERIES", "Paragon Space"), ("description", "A blurb.")));

            Assert.True(result.Success, result.Message);

            var written = await writer.ReadAsync(source);
            Assert.Equal("Paragon Space", written.Tags["SERIES"]);
            Assert.Equal("A blurb.", written.Tags["description"]);
            Assert.Equal(2, written.ChapterCount);
        }

        // ---- failure paths -------------------------------------------------------------

        [EncoderFact]
        public async Task WriteAsync_MissingSource_IsReportedAsUnreadableRatherThanThrowing()
        {
            var result = await BuildWriter().WriteAsync(new TagWriteRequest(
                Path.Combine(_workingDirectory, "not-here.m4b"),
                OutputPath,
                Tags(("title", "Drive")),
                AudiobookFileTags.Empty));

            Assert.False(result.Success);
            Assert.Equal(TagWriteFailureKind.SourceUnreadable, result.FailureKind);
            Assert.False(File.Exists(OutputPath));
        }

        [EncoderFact]
        public async Task WriteAsync_WithNoWriter_RefusesRatherThanReportingAnUnverifiedSuccess()
        {
            // Writing needs no external binary, but reading the result back does. A
            // write that cannot be verified is how this silently does nothing, so it is
            // refused rather than reported as an unverified success.
            var writer = BuildWriter(ffprobePath: null, useDefaultProbe: false);

            var result = await writer.WriteAsync(new TagWriteRequest(
                await WriteBookAsync(),
                OutputPath,
                Tags(("title", "Drive")),
                AudiobookFileTags.Empty));

            Assert.False(result.Success);
            Assert.Equal(TagWriteFailureKind.WriterUnavailable, result.FailureKind);
        }

        [EncoderFact]
        public async Task IsAvailableAsync_NeedsTheVerifier()
        {
            Assert.True(await BuildWriter().IsAvailableAsync());
            Assert.False(await BuildWriter(ffprobePath: null, useDefaultProbe: false).IsAvailableAsync());
        }

        [EncoderFact]
        public async Task WriteAsync_WithNothingToSet_TouchesNothing()
        {
            // The ordinary outcome of a second run. Copying a book-sized file to change
            // nothing is pure cost, and publishing the result would replace a library
            // file for no reason at all.
            var source = await WriteBookAsync();

            var result = await BuildWriter().WriteAsync(new TagWriteRequest(
                source,
                OutputPath,
                Tags(),
                await BuildWriter().ReadAsync(source)));

            Assert.True(result.Success, result.Message);
            Assert.Equal(0, result.TagsWritten);
            Assert.False(File.Exists(OutputPath));
        }

        [EncoderFact]
        public async Task ReadAsync_ReportsTheTagsAFileAlreadyCarries()
        {
            var source = await WriteBookAsync(
                extraMetadataDocument: "album=Whatever the release called it\n");

            var tags = await BuildWriter().ReadAsync(source);

            Assert.Equal("Whatever the release called it", tags.Tags["album"]);

            // The reader is faithful: it reports what ffprobe reports, container facts
            // included, and the planner is what decides none of them are carried forward
            // as tags. The brand is the exception that has to survive, so it is lifted
            // out separately.
            Assert.NotNull(tags.MajorBrand);
        }

        // ---- helpers -------------------------------------------------------------------

        private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
        {
            for (var i = 0; i + needle.Length <= haystack.Length; i++)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CountOccurrences(byte[] haystack, ReadOnlySpan<byte> needle)
        {
            var count = 0;
            for (var i = 0; i + needle.Length <= haystack.Length; i++)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// An <see cref="IFfmpegService"/> that reports fixed binary paths, so the writer
        /// can be driven without the installer or its download.
        /// </summary>
        private sealed class PathResolvedFfmpegService(string? ffmpegPath, string? ffprobePath) : IFfmpegService
        {
            public Task<string?> GetFfmpegPathAsync() => Task.FromResult(ffmpegPath);
            public Task<string?> EnsureFfmpegInstalledAsync() => Task.FromResult(ffmpegPath);
            public Task<string?> GetFfprobePathAsync() => Task.FromResult(ffprobePath);
            public Task<string?> EnsureFfprobeInstalledAsync() => Task.FromResult(ffprobePath);
            public Task<string> GetLicenseAsync() => Task.FromResult(string.Empty);

            public Task<IReadOnlyList<EmbeddedChapter>> ReadChaptersAsync(
                string filePath,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<EmbeddedChapter>>([]);

            public Task<AudioMetadata> RunFfprobeAsync(string filePath) =>
                throw new NotSupportedException();

            public Task<AudioMetadata> RunFfprobeAsync(MetadataFileSource fileSource) =>
                throw new NotSupportedException();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_workingDirectory))
                {
                    Directory.Delete(_workingDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not a test failure.
            }
        }
    }
}
