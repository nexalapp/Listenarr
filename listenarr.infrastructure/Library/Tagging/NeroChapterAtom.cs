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
using System.Text;

namespace Listenarr.Infrastructure.Library.Tagging
{
    /// <summary>
    /// Carries the Nero chapter atom (<c>moov/udta/chpl</c>) across a TagLib# save.
    ///
    /// <para>
    /// TagLib# re-renders the whole <c>udta</c> box when it saves, and any child it has
    /// no class for is copied through <c>UnknownBox</c>. That constructor reads its
    /// payload from wherever the file cursor happens to be rather than from the box's own
    /// data position — and <c>BoxHeader</c> has just read a 32-byte block, so the cursor
    /// sits 24 bytes past the 8-byte header. Every unknown atom is written back shifted
    /// by 24 bytes, and one that ends the file, as <c>chpl</c> does in ffmpeg's output,
    /// is cut 24 bytes short as well. (TagLibSharp 2.3.0; still present on main.)
    /// </para>
    /// <para>
    /// What that does to a book depends on the first chapter's title, because those are
    /// the bytes ffmpeg then reads as the atom's version and chapter count. Sometimes it
    /// parses as no chapters and the QuickTime chapter track quietly takes over; sometimes
    /// it parses as a garbage count and the file is rejected; sometimes it runs off the
    /// end of the file and the file cannot be opened at all. That is Bug B.
    /// </para>
    /// <para>
    /// The atom is captured byte for byte before the save and written back afterwards.
    /// When TagLib# preserved the size, the payload is overwritten in place; when it
    /// truncated the atom at the end of the file, the tail is rebuilt and the enclosing
    /// <c>udta</c> and <c>moov</c> sizes are corrected. Nothing else moves, so no chunk
    /// offsets need touching.
    /// </para>
    /// </summary>
    internal static class NeroChapterAtom
    {
        private const int HeaderLength = 8;

        /// <summary>The atom exactly as it was, and where it sat.</summary>
        internal sealed record Snapshot(byte[] Bytes);

        /// <summary>
        /// The <c>chpl</c> atom, with its header, or null when the file carries none.
        /// </summary>
        public static Snapshot? Capture(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var located = Locate(stream);
            if (located == null)
            {
                return null;
            }

            var (atom, _) = located.Value;
            var bytes = new byte[atom.Size];
            stream.Seek(atom.Position, SeekOrigin.Begin);
            stream.ReadExactly(bytes);
            return new Snapshot(bytes);
        }

        /// <summary>
        /// Put a captured atom back after a save. Throws when the file no longer has the
        /// shape the capture saw, since guessing would only produce a different corruption.
        /// </summary>
        public static void Restore(string path, Snapshot snapshot)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var located = Locate(stream)
                ?? throw new InvalidOperationException(
                    "The chapter atom was dropped by the tag write, so the file cannot be repaired.");

            var (atom, ancestors) = located;
            var expected = snapshot.Bytes.LongLength;

            if (atom.Size == expected)
            {
                stream.Seek(atom.Position, SeekOrigin.Begin);
                stream.Write(snapshot.Bytes);
                return;
            }

            if (atom.Position + atom.Size != stream.Length)
            {
                throw new InvalidOperationException(
                    $"The chapter atom changed from {expected} to {atom.Size} bytes in the middle of the file, so the file cannot be repaired.");
            }

            // Truncated at the end of the file: rewrite the tail and grow every box that
            // contains it. They all end where the file ends, so their sizes are the only
            // thing that changes.
            var delta = expected - atom.Size;
            stream.SetLength(atom.Position);
            stream.Seek(atom.Position, SeekOrigin.Begin);
            stream.Write(snapshot.Bytes);

            foreach (var ancestor in ancestors)
            {
                WriteSize(stream, ancestor, ancestor.Size + delta);
            }
        }

        private readonly record struct Atom(long Position, long Size, int HeaderSize, string Type);

        /// <summary>
        /// Find <c>moov/udta/chpl</c>, returning it and its ancestors nearest first.
        /// </summary>
        private static (Atom Chpl, IReadOnlyList<Atom> Ancestors)? Locate(Stream stream)
        {
            var moov = Find(stream, 0, stream.Length, "moov");
            if (moov == null)
            {
                return null;
            }

            var udta = Find(stream, moov.Value.Position + moov.Value.HeaderSize, End(moov.Value), "udta");
            if (udta == null)
            {
                return null;
            }

            var chpl = Find(stream, udta.Value.Position + udta.Value.HeaderSize, End(udta.Value), "chpl");
            if (chpl == null)
            {
                return null;
            }

            return (chpl.Value, [udta.Value, moov.Value]);
        }

        private static long End(Atom atom) => atom.Position + atom.Size;

        private static Atom? Find(Stream stream, long start, long end, string type)
        {
            var position = start;
            while (position + HeaderLength <= end)
            {
                var atom = ReadHeader(stream, position, end);
                if (atom == null)
                {
                    return null;
                }

                if (atom.Value.Type == type)
                {
                    return atom;
                }

                position = End(atom.Value);
            }

            return null;
        }

        private static Atom? ReadHeader(Stream stream, long position, long end)
        {
            Span<byte> header = stackalloc byte[16];
            stream.Seek(position, SeekOrigin.Begin);
            stream.ReadExactly(header[..HeaderLength]);

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.Latin1.GetString(header[4..8]);
            var headerSize = HeaderLength;

            if (size == 1)
            {
                stream.ReadExactly(header[8..16]);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..16]);
                headerSize = 16;
            }
            else if (size == 0)
            {
                // Runs to the end of its container.
                size = end - position;
            }

            if (size < headerSize || position + size > end)
            {
                return null;
            }

            return new Atom(position, size, headerSize, type);
        }

        private static void WriteSize(Stream stream, Atom atom, long size)
        {
            Span<byte> field = stackalloc byte[8];
            if (atom.HeaderSize == 16)
            {
                BinaryPrimitives.WriteUInt64BigEndian(field, (ulong)size);
                stream.Seek(atom.Position + 8, SeekOrigin.Begin);
                stream.Write(field);
                return;
            }

            if (size > uint.MaxValue)
            {
                throw new InvalidOperationException(
                    "A box grew past what its 32-bit size field can hold, so the file cannot be repaired.");
            }

            BinaryPrimitives.WriteUInt32BigEndian(field[..4], (uint)size);
            stream.Seek(atom.Position, SeekOrigin.Begin);
            stream.Write(field[..4]);
        }
    }
}
