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
    /// The little MP4 box walking the chapter code needs: a sibling search by type, a
    /// header read that understands 64-bit and to-end-of-container sizes, and a size
    /// rewrite. Shared by <see cref="NeroChapterAtom"/> and <see cref="ChapterAtomInspector"/>.
    /// </summary>
    internal static class Mp4Atoms
    {
        internal const int HeaderLength = 8;

        internal readonly record struct Atom(long Position, long Size, int HeaderSize, string Type);

        internal static long End(Atom atom) => atom.Position + atom.Size;

        internal static Atom? Find(Stream stream, long start, long end, string type)
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

        /// <summary>Every direct child of a container, in file order, stopping at the first unreadable header.</summary>
        internal static IEnumerable<Atom> Children(Stream stream, Atom container)
        {
            var position = container.Position + container.HeaderSize;
            var end = End(container);
            while (position + HeaderLength <= end)
            {
                var atom = ReadHeader(stream, position, end);
                if (atom == null)
                {
                    yield break;
                }

                yield return atom.Value;
                position = End(atom.Value);
            }
        }

        internal static Atom? ReadHeader(Stream stream, long position, long end)
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

        internal static void WriteSize(Stream stream, Atom atom, long size)
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
