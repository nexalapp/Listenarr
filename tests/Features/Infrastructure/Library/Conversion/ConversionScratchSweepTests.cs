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
using Listenarr.Application.Audiobooks.Conversion;
using Listenarr.Infrastructure.Library.Conversion;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Conversion
{
    /// <summary>
    /// The scratch sweep keeps a failed conversion's verified encode for a week so a
    /// retry publishes without re-encoding - unless the book has since been converted
    /// by another job, when the kept encode is several hundred megabytes for a retry
    /// with nothing to do. Twenty of those sat in one library's scratch directory.
    /// </summary>
    [Trait("Name", "ConversionScratchSweepTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class ConversionScratchSweepTests : BaseTests, IDisposable
    {
        private readonly Mock<IConversionQueueService> _queue = new();
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly string _scratch = Path.Combine(Path.GetTempPath(), "listenarr-sweep-" + Guid.NewGuid().ToString("N"));

        public ConversionScratchSweepTests()
        {
            Directory.CreateDirectory(_scratch);
            _queue.Setup(q => q.RecoverAbandonedJobsAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _queue.Setup(q => q.ClaimNextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((ConversionJob?)null);
            _queue.Setup(q => q.ClearVerifiedOutputAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        private ConversionJobProcessor BuildProcessor()
        {
            var paths = new Mock<IApplicationPathService>();
            paths.Setup(p => p.ResolveFromConfig(It.IsAny<string[]>())).Returns(_scratch);

            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(IConversionQueueService))).Returns(_queue.Object);
            provider.Setup(p => p.GetService(typeof(IApplicationPathService))).Returns(paths.Object);
            provider.Setup(p => p.GetService(typeof(IAudiobookRepository))).Returns(_audiobooks.Object);

            var scope = new Mock<IServiceScope>();
            scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
            var factory = new Mock<IServiceScopeFactory>();
            factory.Setup(f => f.CreateScope()).Returns(scope.Object);

            return new ConversionJobProcessor(factory.Object, NullLogger<ConversionJobProcessor>.Instance);
        }

        private string GivenKeptEncode(ConversionJob job)
        {
            var path = Path.Combine(_scratch, $"conversion-{job.Id:N}.m4b");
            File.WriteAllText(path, "encoded");
            job.VerifiedOutputPath = path;
            job.VerifiedOutputLength = 7;
            _queue.Setup(q => q.GetJobAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);
            return path;
        }

        private static ConversionJob FailedYesterday(int audiobookId) => new()
        {
            Id = Guid.NewGuid(),
            AudiobookId = audiobookId,
            Status = ConversionJobStatus.Failed,
            CompletedAt = DateTime.UtcNow.AddDays(-1)
        };

        private void GivenBookFiles(int audiobookId, params string[] names)
        {
            var book = new AudiobookBuilder().WithId(audiobookId).WithTitle("Book").Build();
            book.Files = [.. names.Select(name => AudiobookFile.CreateUnresolved("/audiobooks/Author/Book/" + name))];
            _audiobooks.Setup(r => r.GetByIdAsync(audiobookId)).ReturnsAsync(book);
        }

        [Fact]
        public async Task Sweep_KeepsAFreshEncodeWhileTheBookStillHasSources()
        {
            var job = FailedYesterday(5);
            var path = GivenKeptEncode(job);
            GivenBookFiles(5, "Book.mp3");

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            Assert.True(File.Exists(path));
        }

        [Fact]
        public async Task Sweep_DropsAKeptEncodeOnceTheBookIsAlreadyConverted()
        {
            var job = FailedYesterday(5);
            var path = GivenKeptEncode(job);
            GivenBookFiles(5, "Book.m4b");

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            Assert.False(File.Exists(path));
            _queue.Verify(q => q.ClearVerifiedOutputAsync(job.Id, It.IsAny<CancellationToken>()), Times.Once);
        }

        public void Dispose()
        {
            if (Directory.Exists(_scratch))
            {
                Directory.Delete(_scratch, recursive: true);
            }
        }
    }
}
