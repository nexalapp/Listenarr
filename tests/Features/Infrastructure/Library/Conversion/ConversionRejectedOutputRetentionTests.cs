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
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Conversion
{
    /// <summary>
    /// A tag write that produces a file failing inspection keeps that file.
    ///
    /// Tag writing has produced M4Bs that read back as corrupt, and others carrying
    /// chapters they were never given. Neither reproduces from the source, so the only
    /// evidence is the bytes that were written - and those were deleted the instant they
    /// failed, which meant every occurrence destroyed the thing needed to explain it.
    /// </summary>
    [Trait("Name", "ConversionRejectedOutputRetentionTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class ConversionRejectedOutputRetentionTests : BaseTests
    {
        private static ConversionJobProcessor BuildProcessor() => new(
            new Mock<IServiceScopeFactory>().Object,
            NullLogger<ConversionJobProcessor>.Instance);

        [Fact]
        public void TryKeepRejectedOutput_MovesTheFileOutOfTheSweepersReach()
        {
            var directory = Path.Join(Path.GetTempPath(), "listenarr-rejected-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var jobId = Guid.NewGuid();
                var scratch = Path.Join(directory, $"conversion-{jobId:N}.m4b");
                File.WriteAllText(scratch, "rejected bytes");

                Assert.True(BuildProcessor().TryKeepRejectedOutput(jobId, scratch));

                // Gone from the path the sweeper reclaims...
                Assert.False(File.Exists(scratch));

                // ...and still on disk under a name that glob cannot match, which is the
                // whole protection. Nothing is recorded against the job, because the only
                // field that would protect it is VerifiedOutputPath and a retry publishes
                // whatever that names.
                var kept = Directory.GetFiles(directory);
                var survivor = Assert.Single(kept);
                Assert.Equal("rejected bytes", File.ReadAllText(survivor));
                Assert.DoesNotContain(
                    survivor,
                    Directory.GetFiles(directory, "conversion-*.m4b"));
                Assert.Contains(jobId.ToString("N"), Path.GetFileName(survivor));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void TryKeepRejectedOutput_ReportsFailureWhenThereIsNothingToKeep()
        {
            // The caller deletes when this returns false, so "no file" must not be
            // mistaken for "kept".
            var missing = Path.Join(
                Path.GetTempPath(),
                $"conversion-{Guid.NewGuid():N}.m4b");

            Assert.False(BuildProcessor().TryKeepRejectedOutput(Guid.NewGuid(), missing));
        }

        [Fact]
        public void TryKeepRejectedOutput_KeepsEachRejectionSeparately()
        {
            // Two rejections of the same job must not collide: the second would otherwise
            // overwrite the first, and the first is evidence too.
            var directory = Path.Join(Path.GetTempPath(), "listenarr-rejected-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var processor = BuildProcessor();
                var jobId = Guid.NewGuid();

                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var scratch = Path.Join(directory, $"conversion-{jobId:N}.m4b");
                    File.WriteAllText(scratch, $"attempt {attempt}");
                    processor.TryKeepRejectedOutput(jobId, scratch);
                    Thread.Sleep(1100);
                }

                Assert.Equal(2, Directory.GetFiles(directory).Length);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
