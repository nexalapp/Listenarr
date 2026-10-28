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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Conversion
{
    /// <summary>
    /// A repair archives a one-M4B book's original before publishing at its path; when
    /// the publication fails, the original goes back so the book is never left empty.
    /// </summary>
    [Trait("Name", "RepairRollbackTests")]
    [Trait("Category", "Repair")]
    public sealed class RepairRollbackTests : BaseTests
    {
        private readonly string _work = Path.Join(Path.GetTempPath(), $"repair-rollback-{Guid.NewGuid():N}");
        private readonly Mock<IScanQueueService> _scans = new();

        private const string Rejected =
            "The converted file could not be published into the library: database is locked. The original files have been left alone.";

        private ConversionJobProcessor Processor() =>
            new(new Mock<IServiceScopeFactory>().Object, NullLogger<ConversionJobProcessor>.Instance);

        private IServiceProvider Services() =>
            new ServiceCollection().AddSingleton(_scans.Object).BuildServiceProvider();

        [Fact]
        public async Task TheArchivedOriginal_GoesBack_AndTheBookIsRescanned()
        {
            var library = Path.Combine(_work, "library", "Valis.m4b");
            var archive = Path.Combine(_work, "archive", "Valis.m4b");
            Directory.CreateDirectory(Path.GetDirectoryName(library)!);
            Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
            await File.WriteAllTextAsync(archive, "original");
            var book = new AudiobookBuilder().WithId(907).Build();

            var message = await Processor().RestoreArchivedOriginalsAsync(book, [(library, archive)], Rejected, Services());

            Assert.Equal("original", await File.ReadAllTextAsync(library));
            Assert.False(File.Exists(archive));
            _scans.Verify(scans => scans.EnqueueScanAsync(book, null, null), Times.Once);
            Assert.Contains("put back in the library", message);
        }

        [Fact]
        public async Task NothingArchived_LeavesTheMessageAndTheLibraryAlone()
        {
            var message = await Processor().RestoreArchivedOriginalsAsync(
                new AudiobookBuilder().WithId(1).Build(), [], Rejected, Services());

            Assert.Equal(Rejected, message);
            _scans.VerifyNoOtherCalls();
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
