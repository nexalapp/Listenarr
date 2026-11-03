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
using Listenarr.Application.Audiobooks.Audit;
using Listenarr.Application.Audiobooks.Chapters;
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Domain.Audiobooks.Quality;
using Listenarr.Infrastructure.Library.Tagging;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Tagging
{
    /// <summary>
    /// Which kinds the filesystem-mutation gate speaks for. Listening, planning and
    /// probing read a book's files and publish nothing, so a guard about *changing*
    /// them has no say: a probe failed on it reported a tag-write conflict for work
    /// that was never going to write a tag, and was re-queued behind a recovery it
    /// did not need.
    /// </summary>
    [Trait("Name", "TagJobProcessorReadOnlyKindsTests")]
    [Trait("Category", "Tagging")]
    public sealed class TagJobProcessorReadOnlyKindsTests : BaseTests
    {
        private readonly Mock<ITagQueueService> _queue = new();
        private readonly Mock<IMoveQueueService> _moves = new();
        private readonly Queue<TagJob> _pending = new();

        private TagJobProcessor BuildProcessor()
        {
            var audiobooks = new Mock<IAudiobookRepository>();
            audiobooks
                .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int id) => new AudiobookBuilder().WithId(id).WithTitle($"Book {id}").Build());

            // The case that produced the bug: a committed import still owning
            // source-cleanup state for the book.
            _moves
                .Setup(m => m.EnsureFilesystemMutationAllowedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ApplicationConflictException(
                    "registration_recovery_pending",
                    "A committed file import still owns source-cleanup state for this audiobook."));

            var paths = new Mock<IApplicationPathService>();
            paths
                .Setup(p => p.ResolveFromConfig(It.IsAny<string>()))
                .Returns(Path.Combine(Path.GetTempPath(), "listenarr-no-such-scratch"));

            _queue.Setup(q => q.RecoverAbandonedJobsAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _queue
                .Setup(q => q.ClaimNextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _pending.Count > 0 ? _pending.Dequeue() : null);
            _queue.Setup(q => q.HeartbeatAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _queue
                .Setup(q => q.ReportProgressAsync(It.IsAny<Guid>(), It.IsAny<TagJobPhase>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _queue.Setup(q => q.CompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _queue
                .Setup(q => q.FailAsync(It.IsAny<Guid>(), It.IsAny<TagWriteFailureKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var repair = new Mock<IChapterRepairService>();
            repair
                .Setup(r => r.PlanAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, IReadOnlyCollection<int>? _, IProgress<double>? _, CancellationToken _) =>
                    new ChapterRepairPreview(id, []));

            var quality = new Mock<IAudioQualityService>();
            quality
                .Setup(q => q.ProbeAsync(It.IsAny<int>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AudioQualityReport(
                    AudioQuality.Unknown,
                    "nothing to probe in this test",
                    new AudioQualityMetrics(0, null, 0, 0, 0, null, 0),
                    GenerativeRecommended: false,
                    NeverPreDenoise: false));

            var audit = new Mock<IAudioAuditService>();
            audit
                .Setup(a => a.AuditAsync(It.IsAny<int>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                .ThrowsAsync(new InvalidOperationException("no transcriber in this test"));

            var services = new ServiceCollection();
            services.AddSingleton(_queue.Object);
            services.AddSingleton(_moves.Object);
            services.AddSingleton(audiobooks.Object);
            services.AddSingleton(paths.Object);
            services.AddSingleton(repair.Object);
            services.AddSingleton(quality.Object);
            services.AddSingleton(audit.Object);
            services.AddSingleton(new Mock<IToastService>().Object);

            var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            return new TagJobProcessor(scopes, NullLogger<TagJobProcessor>.Instance);
        }

        private void Given(TagJobKind kind) =>
            _pending.Enqueue(new TagJob
            {
                AudiobookId = 1,
                Kind = kind,
                FileCount = 1,
                SelectedFileIdsJson = "[1]"
            });

        [Theory]
        [InlineData(TagJobKind.Plan)]
        [InlineData(TagJobKind.Probe)]
        [InlineData(TagJobKind.Audit)]
        [InlineData(TagJobKind.AuditFurther)]
        public async Task RunCycleAsync_DoesNotAskWhetherAReadOnlyKindMayChangeTheFiles(TagJobKind kind)
        {
            Given(kind);

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _moves.Verify(
                m => m.EnsureFilesystemMutationAllowedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Theory]
        [InlineData(TagJobKind.Plan)]
        [InlineData(TagJobKind.Probe)]
        public async Task RunCycleAsync_FinishesAReadOnlyKindWhileARecoveryIsPending(TagJobKind kind)
        {
            Given(kind);

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _queue.Verify(
                q => q.CompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _queue.Verify(
                q => q.FailAsync(It.IsAny<Guid>(), It.IsAny<TagWriteFailureKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task RunCycleAsync_StillRefusesATagWriteWhileARecoveryIsPending()
        {
            Given(TagJobKind.Tags);

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _moves.Verify(
                m => m.EnsureFilesystemMutationAllowedAsync(1, It.IsAny<CancellationToken>()),
                Times.Once);
            _queue.Verify(
                q => q.FailAsync(
                    It.IsAny<Guid>(),
                    TagWriteFailureKind.Transient,
                    It.Is<string>(error => error.Contains("source-cleanup state")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
