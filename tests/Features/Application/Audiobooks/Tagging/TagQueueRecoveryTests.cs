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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Tagging
{
    /// <summary>
    /// What happens to a job whose lease keeps expiring. A job that is claimed, goes
    /// quiet and is returned over and over holds a worker slot each time and reads as
    /// "Processing" that never moves; past a point it is a failure, not a retry.
    /// </summary>
    [Trait("Name", "TagQueueRecoveryTests")]
    [Trait("Category", "Tagging")]
    public sealed class TagQueueRecoveryTests : BaseTests
    {
        private readonly Mock<ITagJobRepository> _repository = new();
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IAudiobookTagWriter> _writer = new();
        private readonly Mock<IHubBroadcaster> _broadcaster = new();
        private readonly List<TagJob> _failed = [];

        private TagQueueService BuildService()
        {
            _repository
                .Setup(r => r.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Action<TagJob>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, Action<TagJob> mutate, CancellationToken _) =>
                {
                    var job = new TagJob { Id = id, Status = TagJobStatus.Running, AttemptCount = TagQueueService.MaxStrandings };
                    mutate(job);
                    _failed.Add(job);
                    return true;
                });
            return new TagQueueService(
                _repository.Object,
                _audiobooks.Object,
                _configuration.Object,
                _writer.Object,
                _broadcaster.Object,
                TimeProvider.System,
                NullLogger<TagQueueService>.Instance);
        }

        private static TagJob Stranded(int attempts) => new()
        {
            Id = Guid.NewGuid(),
            AudiobookId = 7,
            Kind = TagJobKind.Plan,
            Status = TagJobStatus.Queued,
            AttemptCount = attempts,
        };

        private void GivenReleased(params TagJob[] jobs) =>
            _repository
                .Setup(r => r.ReleaseExpiredLeasesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(jobs);

        [Fact]
        public async Task RecoverAbandonedJobsAsync_LeavesAJobThatHasNotBeenStrandedOften()
        {
            GivenReleased(Stranded(TagQueueService.MaxStrandings - 1));

            await BuildService().RecoverAbandonedJobsAsync();

            Assert.Empty(_failed);
        }

        [Fact]
        public async Task RecoverAbandonedJobsAsync_FailsAJobThatKeepsGoingQuiet()
        {
            var job = Stranded(TagQueueService.MaxStrandings);
            GivenReleased(job);

            await BuildService().RecoverAbandonedJobsAsync();

            var failure = Assert.Single(_failed);
            Assert.Equal(TagJobStatus.Failed, failure.Status);
            Assert.Contains($"{TagQueueService.MaxStrandings} attempts", failure.Error);
        }

        [Fact]
        public async Task RecoverAbandonedJobsAsync_DoesNothingWhenNoLeaseHasExpired()
        {
            GivenReleased();

            await BuildService().RecoverAbandonedJobsAsync();

            Assert.Empty(_failed);
            _repository.Verify(
                r => r.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Action<TagJob>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
