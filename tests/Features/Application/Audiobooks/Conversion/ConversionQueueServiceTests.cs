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
using Listenarr.Application.Audiobooks;
using Listenarr.Application.Audiobooks.Conversion;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Conversion
{
    [Trait("Name", "ConversionQueueServiceTests")]
    [Trait("Category", "Application")]
    public sealed class ConversionQueueServiceTests : BaseTests
    {
        private readonly Mock<IConversionJobRepository> _repository = new();
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IAudiobookConverter> _converter = new();
        private readonly Mock<IHubBroadcaster> _broadcaster = new();
        private readonly TimeProvider _time = TimeProvider.System;

        private ConversionQueueService BuildService() => new(
            _repository.Object,
            _audiobooks.Object,
            _configuration.Object,
            _converter.Object,
            _broadcaster.Object,
            _time,
            NullLogger<ConversionQueueService>.Instance);

        private void GivenSettings(bool conversionEnabled) =>
            _configuration
                .Setup(service => service.GetApplicationSettingsAsync())
                .ReturnsAsync(conversionEnabled
                    ? new ApplicationSettingsBuilder().WithMp3ToM4bConversion().Build()
                    : new ApplicationSettingsBuilder().WithoutMp3ToM4bConversion().Build());

        private void GivenEncoderAvailable(bool available = true) =>
            _converter
                .Setup(converter => converter.IsAvailableAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(available);

        private Audiobook GivenAudiobook(params string[] filePaths)
        {
            var audiobook = new AudiobookBuilder()
                .WithId(7)
                .WithTitle("A Book")
                .WithBasePath("/library/book")
                .Build();

            audiobook.Files = filePaths
                .Select(path =>
                {
                    var file = AudiobookFile.CreateUnresolved(path);
                    return file;
                })
                .ToList();

            _audiobooks.Setup(repository => repository.GetByIdAsync(7)).ReturnsAsync(audiobook);
            return audiobook;
        }

        private void GivenNoActiveJob() =>
            _repository
                .Setup(repository => repository.GetActiveForAudiobookAsync(
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConversionJob?)null);

        private void GivenAddSucceeds() =>
            _repository
                .Setup(repository => repository.AddAsync(
                    It.IsAny<ConversionJob>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConversionJob job, CancellationToken _) => job);

        // ---- what gets queued -------------------------------------------------------

        [Fact]
        public async Task EnqueueAsync_QueuesAnMp3Book_WhenConversionIsEnabled()
        {
            GivenSettings(conversionEnabled: true);
            GivenEncoderAvailable();
            GivenAudiobook("/library/book/01.mp3", "/library/book/02.mp3");
            GivenNoActiveJob();
            GivenAddSucceeds();

            var result = await BuildService().EnqueueAsync(7, ConversionTrigger.Automatic);

            Assert.True(result.Queued);
            _repository.Verify(
                repository => repository.AddAsync(
                    It.Is<ConversionJob>(job => job.SourceFileCount == 2 && job.AudiobookId == 7),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task EnqueueAsync_RefusesAutomatically_WhenTheSettingIsOff()
        {
            GivenSettings(conversionEnabled: false);
            GivenAudiobook("/library/book/01.mp3");

            var result = await BuildService().EnqueueAsync(7, ConversionTrigger.Automatic);

            Assert.Equal(ConversionEnqueueOutcome.Disabled, result.Outcome);
            _repository.Verify(
                repository => repository.AddAsync(It.IsAny<ConversionJob>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task EnqueueAsync_QueuesAManualRequest_EvenWhenTheSettingIsOff()
        {
            // A manual request is an explicit instruction; the setting only governs
            // whether an import queues one on its own.
            GivenSettings(conversionEnabled: false);
            GivenEncoderAvailable();
            GivenAudiobook("/library/book/01.mp3");
            GivenNoActiveJob();
            GivenAddSucceeds();

            var result = await BuildService().EnqueueAsync(7, ConversionTrigger.Manual);

            Assert.True(result.Queued);
        }

        [Fact]
        public async Task EnqueueAsync_RefusesABookThatIsAlreadyAnM4b()
        {
            GivenSettings(conversionEnabled: true);
            GivenAudiobook("/library/book/book.m4b");

            var result = await BuildService().EnqueueAsync(7, ConversionTrigger.Manual);

            Assert.Equal(ConversionEnqueueOutcome.NothingToConvert, result.Outcome);
        }

        [Fact]
        public async Task EnqueueAsync_QueuesASingleMp3()
        {
            // A book merged into one chaptered MP3 is worth converting: MP4 carries the
            // desc atom that ID3 cannot.
            GivenSettings(conversionEnabled: true);
            GivenEncoderAvailable();
            GivenAudiobook("/library/book/whole-book.mp3");
            GivenNoActiveJob();
            GivenAddSucceeds();

            var result = await BuildService().EnqueueAsync(7, ConversionTrigger.Manual);

            Assert.True(result.Queued);
        }

        [Fact]
        public async Task EnqueueAsync_RefusesWithoutAnEncoder_RatherThanQueueingAJobThatMustFail()
        {
            GivenSettings(conversionEnabled: true);
            GivenEncoderAvailable(false);
            GivenAudiobook("/library/book/01.mp3");

            var result = await BuildService().EnqueueAsync(7, ConversionTrigger.Manual);

            Assert.Equal(ConversionEnqueueOutcome.EncoderUnavailable, result.Outcome);
            Assert.Contains("ffmpeg", result.Reason);
            _repository.Verify(
                repository => repository.AddAsync(It.IsAny<ConversionJob>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task EnqueueAsync_ReportsAMissingAudiobook()
        {
            _audiobooks.Setup(repository => repository.GetByIdAsync(7)).ReturnsAsync((Audiobook?)null);

            var result = await BuildService().EnqueueAsync(7, ConversionTrigger.Manual);

            Assert.Equal(ConversionEnqueueOutcome.NotFound, result.Outcome);
        }

        // ---- one conversion per book ------------------------------------------------

        [Fact]
        public async Task EnqueueAsync_DoesNotQueueASecondJobForABookAlreadyConverting()
        {
            GivenSettings(conversionEnabled: true);
            GivenEncoderAvailable();
            GivenAudiobook("/library/book/01.mp3");
            _repository
                .Setup(repository => repository.GetActiveForAudiobookAsync(7, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConversionJob { AudiobookId = 7, Status = ConversionJobStatus.Running });

            var result = await BuildService().EnqueueAsync(7, ConversionTrigger.Manual);

            Assert.Equal(ConversionEnqueueOutcome.AlreadyQueued, result.Outcome);
            _repository.Verify(
                repository => repository.AddAsync(It.IsAny<ConversionJob>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task EnqueueAsync_TreatsALostInsertRaceAsAlreadyQueued()
        {
            // The unique index is the real guard: two callers can both pass the check
            // above and only one insert can win.
            GivenSettings(conversionEnabled: true);
            GivenEncoderAvailable();
            GivenAudiobook("/library/book/01.mp3");
            GivenNoActiveJob();
            _repository
                .Setup(repository => repository.AddAsync(
                    It.IsAny<ConversionJob>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConversionJob?)null);

            var result = await BuildService().EnqueueAsync(7, ConversionTrigger.Manual);

            Assert.Equal(ConversionEnqueueOutcome.AlreadyQueued, result.Outcome);
        }

        [Fact]
        public async Task EnqueueAsync_CarriesTheDeduplicationKeyThatEnforcesOnePerBook()
        {
            GivenSettings(conversionEnabled: true);
            GivenEncoderAvailable();
            GivenAudiobook("/library/book/01.mp3");
            GivenNoActiveJob();
            GivenAddSucceeds();

            await BuildService().EnqueueAsync(7, ConversionTrigger.Manual);

            _repository.Verify(
                repository => repository.AddAsync(
                    It.Is<ConversionJob>(job =>
                        job.ActiveDeduplicationKey == ConversionJob.BuildDeduplicationKey(7)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // ---- failure classification -------------------------------------------------

        [Theory]
        [InlineData(ConversionFailureKind.EncoderUnavailable)]
        [InlineData(ConversionFailureKind.SourceUnreadable)]
        [InlineData(ConversionFailureKind.EncodeFailed)]
        [InlineData(ConversionFailureKind.OutputRejected)]
        public async Task FailAsync_EndsAJobThatNeedsAPerson_RatherThanRetryingOnATimer(
            ConversionFailureKind kind)
        {
            var job = new ConversionJob { AudiobookId = 7, AttemptCount = 1, MaxAttempts = 3 };
            GivenJob(job);

            await BuildService().FailAsync(job.Id, kind, "it went wrong");

            Assert.Equal(ConversionJobStatus.Failed, job.Status);
            Assert.Null(job.NextAttemptAt);
            // The operator can still retry by hand once they have addressed the cause.
            Assert.True(job.CanRetry);
            // Clearing the key frees the unique index so the book can be queued again.
            Assert.Null(job.ActiveDeduplicationKey);
        }

        [Fact]
        public async Task FailAsync_BacksOffAndRetriesATransientFailure()
        {
            var job = new ConversionJob { AudiobookId = 7, AttemptCount = 1, MaxAttempts = 3 };
            GivenJob(job);

            await BuildService().FailAsync(job.Id, ConversionFailureKind.Transient, "the share went away");

            Assert.Equal(ConversionJobStatus.RetryScheduled, job.Status);
            Assert.NotNull(job.NextAttemptAt);
            Assert.True(job.NextAttemptAt > _time.GetUtcNow().UtcDateTime);
        }

        [Fact]
        public async Task FailAsync_StopsRetryingOnceTheAttemptsAreSpent()
        {
            var job = new ConversionJob { AudiobookId = 7, AttemptCount = 3, MaxAttempts = 3 };
            GivenJob(job);

            await BuildService().FailAsync(job.Id, ConversionFailureKind.Transient, "still gone");

            Assert.Equal(ConversionJobStatus.Failed, job.Status);
            Assert.Null(job.NextAttemptAt);
        }

        [Fact]
        public async Task FailAsync_KeepsTheReasonWhereTheOperatorWillSeeIt()
        {
            var job = new ConversionJob { AudiobookId = 7, AttemptCount = 1, MaxAttempts = 3 };
            GivenJob(job);

            await BuildService().FailAsync(
                job.Id,
                ConversionFailureKind.SourceUnreadable,
                "Source file is missing: Chapter 7.mp3");

            Assert.Equal("Source file is missing: Chapter 7.mp3", job.Error);
            Assert.Equal("SourceUnreadable", job.FailureKind);
        }

        // ---- completion -------------------------------------------------------------

        [Fact]
        public async Task CompleteAsync_ReleasesTheDeduplicationKeySoTheBookCanBeConvertedAgain()
        {
            var job = new ConversionJob
            {
                AudiobookId = 7,
                ActiveDeduplicationKey = ConversionJob.BuildDeduplicationKey(7)
            };
            GivenJob(job);

            await BuildService().CompleteAsync(job.Id, "/library/book/book.m4b", 12);

            Assert.Equal(ConversionJobStatus.Completed, job.Status);
            Assert.Equal(100, job.Progress);
            Assert.Equal(12, job.ChapterCount);
            Assert.Null(job.ActiveDeduplicationKey);
            Assert.False(job.CanRetry);
        }

        // ---- keeping a verified encode ------------------------------------------------

        [Fact]
        public async Task RecordVerifiedOutputAsync_RemembersTheEncodeAndItsSize()
        {
            var job = new ConversionJob { AudiobookId = 7 };
            GivenJob(job);

            await BuildService().RecordVerifiedOutputAsync(job.Id, "/scratch/out.m4b", 4096, 12);

            Assert.Equal("/scratch/out.m4b", job.VerifiedOutputPath);
            Assert.Equal(4096, job.VerifiedOutputLength);
            Assert.Equal(12, job.ChapterCount);
        }

        [Fact]
        public async Task ClearVerifiedOutputAsync_ForgetsTheEncode()
        {
            var job = new ConversionJob
            {
                AudiobookId = 7,
                VerifiedOutputPath = "/scratch/out.m4b",
                VerifiedOutputLength = 4096
            };
            GivenJob(job);

            await BuildService().ClearVerifiedOutputAsync(job.Id);

            Assert.Null(job.VerifiedOutputPath);
            Assert.Null(job.VerifiedOutputLength);
        }

        [Fact]
        public async Task CompleteAsync_ForgetsTheKeptEncode()
        {
            // Completing moves the file into the library, so the scratch path it named
            // holds nothing and must not be offered to a later retry.
            var job = new ConversionJob
            {
                AudiobookId = 7,
                VerifiedOutputPath = "/scratch/out.m4b",
                VerifiedOutputLength = 4096
            };
            GivenJob(job);

            await BuildService().CompleteAsync(job.Id, "/library/book/book.m4b", 12);

            Assert.Null(job.VerifiedOutputPath);
            Assert.Null(job.VerifiedOutputLength);
        }

        [Fact]
        public async Task FailAsync_LeavesAKeptEncodeAlone()
        {
            // The point of keeping it is that a retry can publish it, and a retry only
            // happens after a failure.
            var job = new ConversionJob
            {
                AudiobookId = 7,
                AttemptCount = 1,
                MaxAttempts = 3,
                VerifiedOutputPath = "/scratch/out.m4b",
                VerifiedOutputLength = 4096
            };
            GivenJob(job);

            await BuildService().FailAsync(job.Id, ConversionFailureKind.OutputRejected, "rejected");

            Assert.Equal("/scratch/out.m4b", job.VerifiedOutputPath);
            Assert.Equal(4096, job.VerifiedOutputLength);
        }

        [Fact]
        public async Task RetryAsync_KeepsTheEncodeSoItNeedNotBeRepeated()
        {
            var job = new ConversionJob
            {
                AudiobookId = 7,
                Status = ConversionJobStatus.Failed,
                VerifiedOutputPath = "/scratch/out.m4b",
                VerifiedOutputLength = 4096
            };
            GivenJob(job);

            await BuildService().RetryAsync(job.Id);

            Assert.Equal(ConversionJobStatus.Queued, job.Status);
            Assert.Equal("/scratch/out.m4b", job.VerifiedOutputPath);
        }

        private void GivenJob(ConversionJob job)
        {
            _repository
                .Setup(repository => repository.GetAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(job);
            _repository
                .Setup(repository => repository.UpdateAsync(
                    job.Id,
                    It.IsAny<Action<ConversionJob>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, Action<ConversionJob> mutate, CancellationToken _) =>
                {
                    mutate(job);
                    return true;
                });
        }
        [Fact]
        [Trait("Method", "CancelAsync")]
        public async Task CancelAsync_MovesARunningJobOffRunningSoItsWorkerStops()
        {
            // The worker is stopped by losing its lease, and a lease is only renewable on
            // a Running row. Moving the status is therefore the whole mechanism, so this
            // asserts the status rather than any call into the worker.
            var job = new ConversionJob
            {
                Id = Guid.NewGuid(),
                AudiobookId = 7,
                Status = ConversionJobStatus.Running,
                ActiveDeduplicationKey = ConversionJob.BuildDeduplicationKey(7)
            };
            _repository
                .Setup(repo => repo.GetAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(job);
            ConversionJob? mutated = null;
            _repository
                .Setup(repo => repo.UpdateAsync(job.Id, It.IsAny<Action<ConversionJob>>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, Action<ConversionJob>, CancellationToken>((_, mutate, _) =>
                {
                    mutate(job);
                    mutated = job;
                })
                .ReturnsAsync(true);

            var result = await BuildService().CancelAsync(job.Id);

            Assert.True(result.Succeeded);
            Assert.NotNull(mutated);
            Assert.Equal(ConversionJobStatus.Cancelled, mutated!.Status);
            Assert.Null(mutated.ActiveDeduplicationKey);
        }

        [Fact]
        [Trait("Method", "CancelAsync")]
        public async Task CancelAsync_RefusesAJobThatHasAlreadyFinished()
        {
            var job = new ConversionJob
            {
                Id = Guid.NewGuid(),
                AudiobookId = 7,
                Status = ConversionJobStatus.Failed
            };
            _repository
                .Setup(repo => repo.GetAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(job);

            var result = await BuildService().CancelAsync(job.Id);

            Assert.Equal(JobControlOutcome.AlreadyTerminal, result.Outcome);
            _repository.Verify(
                repo => repo.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Action<ConversionJob>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        [Trait("Method", "DismissAsync")]
        public async Task DismissAsync_RemovesAFailedJobSoItsKeptEncodeIsSwept()
        {
            var job = new ConversionJob
            {
                Id = Guid.NewGuid(),
                AudiobookId = 7,
                Status = ConversionJobStatus.Failed,
                VerifiedOutputPath = "/scratch/conversion-abc.m4b"
            };
            _repository
                .Setup(repo => repo.GetAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(job);
            _repository
                .Setup(repo => repo.DeleteAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await BuildService().DismissAsync(job.Id);

            Assert.True(result.Succeeded);
            _repository.Verify(repo => repo.DeleteAsync(job.Id, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        [Trait("Method", "DismissAsync")]
        public async Task DismissAsync_RefusesAJobThatIsStillRunning()
        {
            var job = new ConversionJob
            {
                Id = Guid.NewGuid(),
                AudiobookId = 7,
                Status = ConversionJobStatus.Running
            };
            _repository
                .Setup(repo => repo.GetAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(job);

            var result = await BuildService().DismissAsync(job.Id);

            Assert.Equal(JobControlOutcome.StillActive, result.Outcome);
            _repository.Verify(repo => repo.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

    }
}
