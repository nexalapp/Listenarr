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
using Listenarr.Infrastructure.Library.Conversion;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Conversion
{
    /// <summary>
    /// Reassembly joins chunks with the concat demuxer, which reads every chunk in the
    /// first one's format, so each chunk's real format has to be known first: a 16-bit
    /// chunk among float ones counts as half its length.
    /// </summary>
    [Trait("Name", "RepairChunkFormatTests")]
    [Trait("Category", "Repair")]
    public sealed class RepairChunkFormatTests : BaseTests
    {
        private readonly string _work = Path.Join(Path.GetTempPath(), $"chunk-format-{Guid.NewGuid():N}");

        public RepairChunkFormatTests()
        {
            Directory.CreateDirectory(_work);
        }

        private string Wave(int format, int bits, bool extensible = false, bool leadingChunk = false, int samples = 0)
        {
            var path = Path.Combine(_work, $"{Guid.NewGuid():N}.wav");
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write("RIFF"u8);
            writer.Write(0u);
            writer.Write("WAVE"u8);
            if (leadingChunk)
            {
                // An odd-sized chunk before fmt, padded to even as RIFF requires.
                writer.Write("LIST"u8);
                writer.Write(3u);
                writer.Write(new byte[4]);
            }

            writer.Write("fmt "u8);
            writer.Write(extensible ? 40u : 16u);
            writer.Write((ushort)(extensible ? 0xFFFE : format));
            writer.Write((ushort)1);
            writer.Write(44_100u);
            writer.Write((uint)(44_100 * bits / 8));
            writer.Write((ushort)(bits / 8));
            writer.Write((ushort)bits);
            if (extensible)
            {
                writer.Write((ushort)22);
                writer.Write((ushort)bits);
                writer.Write(4u);
                writer.Write((ushort)format);
                writer.Write(new byte[14]);
            }

            writer.Write("data"u8);
            writer.Write((uint)(samples * bits / 8));
            writer.Write(new byte[samples * bits / 8]);
            return path;
        }

        [Fact]
        public void TheLocalModelsFloatOutput_IsTheCommonFormat()
        {
            Assert.Equal(ConversionJobProcessor.WaveFormat.Canonical, ConversionJobProcessor.ReadWaveHeader(Wave(3, 32))?.Format);
            Assert.Equal(ConversionJobProcessor.WaveFormat.Canonical, ConversionJobProcessor.ReadWaveHeader(Wave(3, 32, extensible: true))?.Format);
            Assert.Equal(ConversionJobProcessor.WaveFormat.Canonical, ConversionJobProcessor.ReadWaveHeader(Wave(3, 32, leadingChunk: true))?.Format);
        }

        [Fact]
        public void A16BitChunk_FromTheGpuOrAnOriginalSplicedBack_IsNot()
        {
            var format = ConversionJobProcessor.ReadWaveHeader(Wave(1, 16))?.Format;
            Assert.Equal(new ConversionJobProcessor.WaveFormat(1, 1, 44_100, 16), format);
            Assert.NotEqual(ConversionJobProcessor.WaveFormat.Canonical, format);
        }

        [Fact]
        public void TheSamplesLength_IsReadFromTheDataChunk()
        {
            // A tenth of a second over, as the CPU model sometimes returns: past the
            // millisecond a chunk may differ from its planned length.
            var header = ConversionJobProcessor.ReadWaveHeader(Wave(3, 32, extensible: true, samples: 44_100 + 4_410));
            Assert.Equal(TimeSpan.FromSeconds(1.1), header!.Value.Duration);
        }

        [Fact]
        public void SomethingThatIsNotAWave_ReadsAsNothing()
        {
            var path = Path.Combine(_work, "not.wav");
            File.WriteAllText(path, "<html>not audio</html>");
            Assert.Null(ConversionJobProcessor.ReadWaveHeader(path));
        }

        public override async Task DisposeAsync()
        {
            try
            {
                Directory.Delete(_work, recursive: true);
            }
            catch (IOException)
            {
            }

            await base.DisposeAsync();
        }
    }
}
