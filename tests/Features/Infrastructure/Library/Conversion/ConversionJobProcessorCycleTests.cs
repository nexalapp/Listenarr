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
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Conversion
{
    /// <summary>
    /// The cycle's own behaviour: when it reclaims stranded jobs, and how it isolates the
    /// pieces that run at the same time.
    ///
    /// These cover a live incident. A conversion's heartbeat and its progress reporting
    /// shared one DI scope, so they shared one EF DbContext, which is not thread-safe.
    /// An ordinary overlap threw "A second operation was started on this context
    /// instance"; the failure handler then used the same context and threw again, so the
    /// job could not even be marked Failed. It sat Running for eleven hours - because
    /// recovery only ran once per cycle, and a cycle lasts until the queue is empty.
    /// </summary>
    [Trait("Name", "ConversionJobProcessorCycleTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class ConversionJobProcessorCycleTests : BaseTests
    {
        private readonly Mock<IConversionQueueService> _queue = new();
        private readonly List<string> _calls = [];
        private int _scopesCreated;

        /// <summary>
        /// Hands out a fresh scope every time, each resolving the same queue mock so calls
        /// are simple to verify. Anything else resolves to null, so
        /// <c>GetRequiredService</c> throws - which is how a job is made to fail inside
        /// its body without standing up the entire conversion service graph.
        /// </summary>
        private IServiceScopeFactory BuildScopeFactory()
        {
            var factory = new Mock<IServiceScopeFactory>();
            factory.Setup(f => f.CreateScope()).Returns(() =>
            {
                _scopesCreated++;

                var provider = new Mock<IServiceProvider>();
                provider
                    .Setup(p => p.GetService(typeof(IConversionQueueService)))
                    .Returns(_queue.Object);

                var scope = new Mock<IServiceScope>();
                scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
                return scope.Object;
            });

            return factory.Object;
        }

        private ConversionJobProcessor BuildProcessor() => new(
            BuildScopeFactory(),
            NullLogger<ConversionJobProcessor>.Instance);

        private void RecordOrdering()
        {
            _queue
                .Setup(q => q.RecoverAbandonedJobsAsync(It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("recover"))
                .Returns(Task.CompletedTask);
        }

        private void GivenClaims(params ConversionJob[] jobs)
        {
            var remaining = new Queue<ConversionJob>(jobs);
            _queue
                .Setup(q => q.ClaimNextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("claim"))
                .ReturnsAsync(() => remaining.Count > 0 ? remaining.Dequeue() : null);
        }

        private static ConversionJob AJob() => new()
        {
            Id = Guid.NewGuid(),
            AudiobookId = 267,
            Status = ConversionJobStatus.Running
        };

        [Fact]
        public async Task RunCycle_ReclaimsStrandedJobsBeforeItClaimsWork()
        {
            RecordOrdering();
            GivenClaims();

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            Assert.Equal(["recover", "claim"], _calls);
        }

        [Fact]
        public async Task RunCycle_ReclaimsBeforeEveryClaimRatherThanOncePerCycle()
        {
            // The reason the stranded job waited eleven hours: a cycle runs until the
            // queue is empty, so a library's worth of conversions is one cycle lasting
            // most of a day. Recovery at the top of that cycle never comes round again.
            RecordOrdering();
            GivenClaims(AJob(), AJob());

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            Assert.Equal(["recover", "claim", "recover", "claim", "recover", "claim"], _calls);
        }

        [Fact]
        public async Task RunCycle_GivesTheHeartbeatAndTheFailurePathScopesOfTheirOwn()
        {
            // One job that fails inside its body accounts for four scopes: the cycle's,
            // the job's, the heartbeat's, and the one the failure is recorded through.
            // Before the fix the heartbeat shared the job's - which is what let a lease
            // renewal and a progress report touch one DbContext at the same time - and
            // the failure was recorded through it too.
            RecordOrdering();
            GivenClaims(AJob());

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            Assert.True(
                _scopesCreated >= 4,
                $"expected at least 4 scopes (cycle, job, heartbeat, failure), saw {_scopesCreated}");
        }

        [Fact]
        public async Task RunCycle_StillMarksAJobFailedWhenItsOwnBodyThrew()
        {
            // The part that turned a failure into a stranded job: recording the failure
            // went through the same context that had just thrown, so it threw too and the
            // job stayed Running with nothing left to move it.
            RecordOrdering();
            var job = AJob();
            GivenClaims(job);

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _queue.Verify(
                q => q.FailAsync(
                    job.Id,
                    ConversionFailureKind.Unknown,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RunCycle_KeepsGoingAfterAJobFails()
        {
            // One poisoned job used to take the whole cycle down with it.
            RecordOrdering();
            GivenClaims(AJob(), AJob());

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _queue.Verify(
                q => q.FailAsync(
                    It.IsAny<Guid>(),
                    ConversionFailureKind.Unknown,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }
}
