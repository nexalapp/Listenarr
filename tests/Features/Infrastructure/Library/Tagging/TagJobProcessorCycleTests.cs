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
using Listenarr.Application.Audiobooks.Chapters;
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Infrastructure.Library.Tagging;
using Listenarr.Infrastructure.Library.Transcription;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Tagging
{
    /// <summary>
    /// The cycle's concurrency: planning jobs share the worker, tag writes do not.
    /// </summary>
    [Trait("Name", "TagJobProcessorCycleTests")]
    [Trait("Category", "Tagging")]
    public sealed class TagJobProcessorCycleTests : BaseTests
    {
        private readonly Mock<ITagQueueService> _queue = new();
        private readonly Mock<IChapterRepairService> _repair = new();
        private readonly Queue<TagJob> _pending = new();
        private readonly object _lock = new();
        private int _running;
        private int _peak;

        private IServiceScopeFactory BuildScopeFactory()
        {
            var audiobooks = new Mock<IAudiobookRepository>();
            audiobooks
                .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int id) => new AudiobookBuilder().WithId(id).WithTitle($"Book {id}").Build());
            var moves = new Mock<IMoveQueueService>();
            moves
                .Setup(m => m.EnsureFilesystemMutationAllowedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var paths = new Mock<IApplicationPathService>();
            paths.Setup(p => p.ResolveFromConfig(It.IsAny<string>())).Returns(Path.Combine(Path.GetTempPath(), "listenarr-no-such-scratch"));

            _queue.Setup(q => q.RecoverAbandonedJobsAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _queue
                .Setup(q => q.ClaimNextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    lock (_lock)
                    {
                        return _pending.Count > 0 ? _pending.Dequeue() : null;
                    }
                });
            _queue.Setup(q => q.HeartbeatAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _queue
                .Setup(q => q.ReportProgressAsync(It.IsAny<Guid>(), It.IsAny<TagJobPhase>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _queue.Setup(q => q.CompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            _repair
                .Setup(r => r.PlanAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns(async (int id, IReadOnlyCollection<int>? _, IProgress<double>? _, CancellationToken ct) =>
                {
                    lock (_lock)
                    {
                        _running++;
                        _peak = Math.Max(_peak, _running);
                    }

                    try
                    {
                        // Long enough for every slot to be claimed while this one is still listening.
                        await Task.Delay(300, ct);
                    }
                    finally
                    {
                        lock (_lock)
                        {
                            _running--;
                        }
                    }

                    return new ChapterRepairPreview(id, []);
                });

            var services = new ServiceCollection();
            services.AddSingleton(_queue.Object);
            services.AddSingleton(_repair.Object);
            services.AddSingleton(audiobooks.Object);
            services.AddSingleton(moves.Object);
            services.AddSingleton(paths.Object);
            services.AddSingleton(new Mock<IToastService>().Object);
            return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }

        private TagJobProcessor BuildProcessor() => new(BuildScopeFactory(), NullLogger<TagJobProcessor>.Instance);

        private void GivenPlanJobs(int count)
        {
            for (var i = 1; i <= count; i++)
            {
                _pending.Enqueue(new TagJob { AudiobookId = i, Kind = TagJobKind.Plan, FileCount = 1, SelectedFileIdsJson = "[1]" });
            }
        }

        [Fact]
        public async Task RunCycleAsync_RunsAsManyPlansAtOnceAsThereAreSlots()
        {
            GivenPlanJobs(6);

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            Assert.Equal(Math.Min(6, TagJobProcessor.PlanningSlots), _peak);
            _queue.Verify(q => q.CompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(6));
        }

        [Fact]
        public async Task RunCycleAsync_ReturnsOnlyWhenEveryPlanItStartedHasFinished()
        {
            GivenPlanJobs(3);

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            Assert.Equal(0, _running);
        }

        [Fact]
        public async Task RunCycleAsync_FailsAJobCancelledBySomethingInsideIt()
        {
            // An internal timeout says so with OperationCanceledException, which used to
            // read as "the lease was lost" — leaving the row Running for a worker that
            // was never coming, to be claimed and abandoned for ever.
            GivenPlanJobs(1);
            var processor = BuildProcessor();
            // After the processor is built, so this setup is the one that stands.
            _repair
                .Setup(r => r.PlanAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("a decode gave up"));

            await processor.RunCycleAsync(CancellationToken.None);

            _queue.Verify(
                q => q.FailAsync(It.IsAny<Guid>(), It.IsAny<TagWriteFailureKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _queue.Verify(
                q => q.CompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public void PlanningSlots_FollowTheTranscriberSlots()
        {
            Assert.Equal(TranscriptionParallelism.Slots, TagJobProcessor.PlanningSlots);
        }
    }
}
