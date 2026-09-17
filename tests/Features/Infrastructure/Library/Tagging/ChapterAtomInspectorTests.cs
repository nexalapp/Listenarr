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
using System.Buffers.Binary;
using Listenarr.Infrastructure.Library.Tagging;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Tagging
{
    [Trait("Name", "ChapterAtomInspectorTests")]
    [Trait("Category", "Tagging")]
    public sealed class ChapterAtomInspectorTests : BaseTests, IDisposable
    {
        private readonly string _workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "listenarr-chapters-" + Guid.NewGuid().ToString("N"));

        /// <summary>A Nero payload exactly as ffmpeg's muxer lays it out.</summary>
        private static byte[] NeroPayload(params (TimeSpan Start, string Title)[] chapters)
        {
            var stream = new MemoryStream();
            stream.Write([1, 0, 0, 0, 0, 0, 0, 0]);
            stream.WriteByte((byte)chapters.Length);
            Span<byte> start = stackalloc byte[8];
            foreach (var (when, title) in chapters)
            {
                BinaryPrimitives.WriteInt64BigEndian(start, when.Ticks);
                stream.Write(start);
                var bytes = System.Text.Encoding.UTF8.GetBytes(title);
                stream.WriteByte((byte)bytes.Length);
                stream.Write(bytes);
            }

            return stream.ToArray();
        }

        [Fact]
        public void Validate_AcceptsWhatFfmpegWrites()
        {
            var payload = NeroPayload(
                (TimeSpan.Zero, "One"),
                (TimeSpan.FromMinutes(20), "Two"),
                (TimeSpan.FromMinutes(45), "Three"));

            var error = ChapterAtomInspector.Validate(payload, TimeSpan.FromHours(1), out var count);

            Assert.Null(error);
            Assert.Equal(3, count);
        }

        [Fact]
        public void Validate_RejectsTheShiftedForm()
        {
            // 24 bytes in, the "version" is the middle of the first timestamp or title.
            var payload = NeroPayload((TimeSpan.Zero, "A long enough chapter title"), (TimeSpan.FromMinutes(20), "Two"));
            var shifted = payload.AsSpan(24).ToArray();

            var error = ChapterAtomInspector.Validate(shifted, TimeSpan.FromHours(1), out _);

            Assert.NotNull(error);
        }

        [Fact]
        public void Validate_RejectsATitleRunningPastTheAtom()
        {
            var payload = NeroPayload((TimeSpan.Zero, "One"));
            var truncated = payload.AsSpan(0, payload.Length - 2).ToArray();

            var error = ChapterAtomInspector.Validate(truncated, TimeSpan.FromHours(1), out _);

            Assert.Contains("runs past the end", error);
        }

        [Fact]
        public void Validate_RejectsMarksOutOfOrder()
        {
            var payload = NeroPayload((TimeSpan.FromMinutes(20), "Two"), (TimeSpan.Zero, "One"));
            Assert.Contains("starts before", ChapterAtomInspector.Validate(payload, TimeSpan.FromHours(1), out _));
        }

        [Fact]
        public void Validate_RejectsMarksPastTheEnd()
        {
            var payload = NeroPayload((TimeSpan.Zero, "One"), (TimeSpan.FromHours(3), "Two"));
            Assert.Contains("past the end of the audio", ChapterAtomInspector.Validate(payload, TimeSpan.FromHours(1), out _));
        }

        [Fact]
        public void Validate_RejectsTrailingBytes()
        {
            var payload = NeroPayload((TimeSpan.Zero, "One")).Concat(new byte[] { 0, 0 }).ToArray();
            Assert.Contains("trailing", ChapterAtomInspector.Validate(payload, TimeSpan.FromHours(1), out _));
        }

        [Fact]
        public void RecoverShifted_FindsTheIntactRunInsideAShiftedPayload()
        {
            // Three saves: 72 bytes gone off the front and the atom padded to its old
            // size. What is left starts inside the second title.
            var payload = NeroPayload(
                (TimeSpan.Zero, "Chapter 001  - 00:00:38"),
                (TimeSpan.FromMinutes(1), "Chapter 002  - 00:07:48"),
                (TimeSpan.FromMinutes(9), "Chapter 003  - 00:06:20"),
                (TimeSpan.FromMinutes(15), "Chapter 004  - 00:04:54"),
                (TimeSpan.FromMinutes(20), "Chapter 005  - 00:05:00"),
                (TimeSpan.FromMinutes(25), "Chapter 006  - 00:05:00"));
            var shifted = new byte[payload.Length];
            Array.Copy(payload, 72, shifted, 0, payload.Length - 72);

            var recovered = ChapterAtomInspector.RecoverShifted(shifted, TimeSpan.FromMinutes(30));

            Assert.NotNull(recovered);
            Assert.Equal(
                ["Chapter 003  - 00:06:20", "Chapter 004  - 00:04:54", "Chapter 005  - 00:05:00", "Chapter 006  - 00:05:00"],
                recovered.Select(c => c.Title));
            Assert.Equal(TimeSpan.FromMinutes(9), recovered[0].Start);
            Assert.Equal(TimeSpan.FromMinutes(30), recovered[^1].End);
        }

        [Fact]
        public void RecoverShifted_GivesUpOnNoise()
        {
            var noise = new byte[200];
            new Random(7).NextBytes(noise);
            Assert.Null(ChapterAtomInspector.RecoverShifted(noise, TimeSpan.FromMinutes(30)));
        }

        [EncoderFact]
        public async Task Inspect_ReadsWhatFfmpegWrote()
        {
            var path = await M4bFixtures.WriteBookAsync(_workingDirectory, chapters: 4, seconds: 8);

            var state = ChapterAtomInspector.Inspect(path);

            Assert.NotNull(state);
            Assert.True(state.HasNeroAtom);
            Assert.Null(state.NeroAtomError);
            Assert.Equal(4, state.NeroChapterCount);
            Assert.True(state.HasChapterTrack);
        }

        [EncoderFact]
        public async Task Inspect_SeesNoStructuresInAnUnchapteredFile()
        {
            var path = await M4bFixtures.WriteBookAsync(_workingDirectory, seconds: 4);

            var state = ChapterAtomInspector.Inspect(path);

            Assert.NotNull(state);
            Assert.False(state.HasNeroAtom);
            Assert.False(state.HasChapterTrack);
        }

        [EncoderFact]
        public async Task Inspect_CatchesTheTagLibShift()
        {
            var path = await M4bFixtures.WriteBookAsync(_workingDirectory, chapters: 4, seconds: 8);
            M4bFixtures.ShiftChapterAtom(path);

            var state = ChapterAtomInspector.Inspect(path);

            Assert.NotNull(state);
            Assert.True(state.HasNeroAtom);
            Assert.NotNull(state.NeroAtomError);
            // The QuickTime track is untouched by the bug, which is what makes repair possible.
            Assert.True(state.HasChapterTrack);
        }

        public void Dispose()
        {
            if (Directory.Exists(_workingDirectory))
            {
                Directory.Delete(_workingDirectory, recursive: true);
            }
        }
    }
}
