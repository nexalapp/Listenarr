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
using Listenarr.Domain.Audiobooks.Chapters;

namespace Listenarr.Infrastructure.Library.Tagging
{
    /// <summary>
    /// Reads what the container's own chapter structures say, without ffprobe.
    ///
    /// <para>
    /// ffprobe is the wrong witness for a broken <c>chpl</c> atom: when the atom does not
    /// parse it falls back to the QuickTime chapter track and reports a healthy list, and
    /// when it half-parses it reports whatever count the garbage happened to spell. Plex
    /// and Prologue read the atom directly, so the atom is what has to be checked. The
    /// Nero layout is ffmpeg's: a full-box header, four reserved bytes, a one-byte count,
    /// then per chapter an eight-byte start in 100ns units, a one-byte title length and
    /// the title.
    /// </para>
    /// </summary>
    internal static class ChapterAtomInspector
    {
        private const int MaxChapters = 255;

        /// <summary>Inspect a file, or null when it has no <c>moov</c> to inspect.</summary>
        public static ChapterAtomState? Inspect(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Inspect(stream);
        }

        internal static ChapterAtomState? Inspect(Stream stream)
        {
            var moov = Mp4Atoms.Find(stream, 0, stream.Length, "moov");
            if (moov == null)
            {
                return null;
            }

            var duration = ReadDuration(stream, moov.Value);
            var hasChapterTrack = false;
            Mp4Atoms.Atom? udta = null;

            foreach (var child in Mp4Atoms.Children(stream, moov.Value))
            {
                if (child.Type == "udta")
                {
                    udta ??= child;
                }
                else if (child.Type == "trak" && ReferencesChapterTrack(stream, child))
                {
                    hasChapterTrack = true;
                }
            }

            var chpl = udta == null
                ? null
                : Mp4Atoms.Find(stream, udta.Value.Position + udta.Value.HeaderSize, Mp4Atoms.End(udta.Value), "chpl");

            if (chpl == null)
            {
                return new ChapterAtomState(false, null, 0, hasChapterTrack);
            }

            var payload = new byte[chpl.Value.Size - chpl.Value.HeaderSize];
            stream.Seek(chpl.Value.Position + chpl.Value.HeaderSize, SeekOrigin.Begin);
            stream.ReadExactly(payload);

            var error = Validate(payload, duration, out var count);
            return new ChapterAtomState(true, error, count, hasChapterTrack);
        }

        /// <summary>
        /// Parse a Nero payload. Returns the reason it is unreadable, or null with the
        /// chapter count when it is sound.
        /// </summary>
        internal static string? Validate(ReadOnlySpan<byte> payload, TimeSpan duration, out int count)
        {
            count = 0;
            if (payload.Length < 9)
            {
                return $"the atom is {payload.Length} byte(s), too short to hold a chapter count";
            }

            var version = payload[0];
            if (version > 1)
            {
                return $"version byte is {version}";
            }

            // Version 0 has no reserved word; ffmpeg only ever writes version 1.
            var offset = version == 1 ? 8 : 4;
            if (payload.Length <= offset)
            {
                return "the atom ends before its chapter count";
            }

            var declared = payload[offset++];
            var previous = -1L;
            for (var index = 0; index < declared; index++)
            {
                if (offset + 9 > payload.Length)
                {
                    return $"chapter {index + 1} of {declared} runs past the end of the atom";
                }

                var start = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(offset, 8));
                offset += 8;
                var titleLength = payload[offset++];
                if (offset + titleLength > payload.Length)
                {
                    return $"the title of chapter {index + 1} runs past the end of the atom";
                }

                offset += titleLength;

                if (start < previous)
                {
                    return $"chapter {index + 1} starts before chapter {index}";
                }

                if (duration > TimeSpan.Zero && start > (duration + TimeSpan.FromSeconds(1)).Ticks)
                {
                    return $"chapter {index + 1} starts past the end of the audio";
                }

                previous = start;
            }

            if (offset != payload.Length)
            {
                return $"{payload.Length - offset} trailing byte(s) after the last chapter";
            }

            count = declared;
            return null;
        }

        private static TimeSpan ReadDuration(Stream stream, Mp4Atoms.Atom moov)
        {
            var mvhd = Mp4Atoms.Find(stream, moov.Position + moov.HeaderSize, Mp4Atoms.End(moov), "mvhd");
            if (mvhd == null)
            {
                return TimeSpan.Zero;
            }

            Span<byte> body = stackalloc byte[32];
            stream.Seek(mvhd.Value.Position + mvhd.Value.HeaderSize, SeekOrigin.Begin);
            var available = (int)Math.Min(body.Length, mvhd.Value.Size - mvhd.Value.HeaderSize);
            stream.ReadExactly(body[..available]);

            var version = body[0];
            uint timescale;
            long duration;
            if (version == 1 && available >= 32)
            {
                timescale = BinaryPrimitives.ReadUInt32BigEndian(body[20..24]);
                duration = BinaryPrimitives.ReadInt64BigEndian(body[24..32]);
            }
            else if (version == 0 && available >= 20)
            {
                timescale = BinaryPrimitives.ReadUInt32BigEndian(body[12..16]);
                duration = BinaryPrimitives.ReadUInt32BigEndian(body[16..20]);
            }
            else
            {
                return TimeSpan.Zero;
            }

            return timescale == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(duration / (double)timescale);
        }

        /// <summary>A track that names another as its chapter track through <c>tref/chap</c>.</summary>
        private static bool ReferencesChapterTrack(Stream stream, Mp4Atoms.Atom trak)
        {
            var tref = Mp4Atoms.Find(stream, trak.Position + trak.HeaderSize, Mp4Atoms.End(trak), "tref");
            if (tref == null)
            {
                return false;
            }

            var chap = Mp4Atoms.Find(stream, tref.Value.Position + tref.Value.HeaderSize, Mp4Atoms.End(tref.Value), "chap");
            return chap != null && chap.Value.Size > chap.Value.HeaderSize;
        }
    }
}
