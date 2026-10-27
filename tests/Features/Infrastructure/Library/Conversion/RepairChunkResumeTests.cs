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
using System.Text.Json;
using Listenarr.Domain.Audiobooks.Repair;
using Listenarr.Infrastructure.Library.Conversion;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Conversion
{
    /// <summary>
    /// Shortening the chunks must not throw away the ones already enhanced on the old
    /// grid: those were hours of compute each.
    /// </summary>
    [Trait("Name", "RepairChunkResumeTests")]
    [Trait("Category", "Repair")]
    public sealed class RepairChunkResumeTests : BaseTests
    {
        private readonly string _work = Path.Join(Path.GetTempPath(), $"repair-resume-{Guid.NewGuid():N}");

        private ConversionSource GivenSource()
        {
            Directory.CreateDirectory(_work);
            var path = Path.Combine(_work, "book.m4b");
            File.WriteAllText(path, "audio");
            return new ConversionSource(path, "book.m4b", TimeSpan.FromHours(20), 64_000, 22_050, 1);
        }

        private void GivenManifest(ConversionSource source, object extra)
        {
            var info = new FileInfo(source.FullPath);
            var fields = new Dictionary<string, object>
            {
                ["SourcePath"] = source.FullPath,
                ["SourceLength"] = info.Length,
                ["SourceLastWriteUtc"] = info.LastWriteTimeUtc,
                ["EnhancerVersion"] = "v1",
                ["ChunkCount"] = 61,
                ["Done"] = Enumerable.Range(0, 20).ToArray()
            };
            foreach (var property in extra.GetType().GetProperties())
            {
                fields[property.Name] = property.GetValue(extra)!;
            }

            File.WriteAllText(Path.Combine(_work, "manifest.json"), JsonSerializer.Serialize(fields));
        }

        [Fact]
        public async Task AManifestFromBeforeTheChange_KeepsItsTwentyMinuteGrid()
        {
            var source = GivenSource();
            GivenManifest(source, new { });

            Assert.Equal(
                RepairChunkPlanner.LegacyChunkLength,
                await ConversionJobProcessor.ResumableChunkLengthAsync(_work, source, "v1"));
        }

        [Fact]
        public async Task AManifestForAnotherModel_IsCutAfreshAtTheNewLength()
        {
            var source = GivenSource();
            GivenManifest(source, new { });

            Assert.Equal(
                RepairChunkPlanner.ChunkLength,
                await ConversionJobProcessor.ResumableChunkLengthAsync(_work, source, "v2"));
        }

        [Fact]
        public async Task ANewRepair_UsesTheNewLength_AndARecordedOneIsHonoured()
        {
            var source = GivenSource();
            Assert.Equal(RepairChunkPlanner.ChunkLength, await ConversionJobProcessor.ResumableChunkLengthAsync(_work, source, "v1"));

            GivenManifest(source, new { ChunkSeconds = 300 });
            Assert.Equal(TimeSpan.FromMinutes(5), await ConversionJobProcessor.ResumableChunkLengthAsync(_work, source, "v1"));
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
