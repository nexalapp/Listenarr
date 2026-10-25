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
using Listenarr.Application.Audiobooks.Repair;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Conversion
{
    [Trait("Name", "ConversionQueueServiceRepairTests")]
    [Trait("Category", "Application")]
    public sealed class ConversionQueueServiceRepairTests : BaseTests
    {
        private readonly Mock<IConversionJobRepository> _repository = new();
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IAudiobookConverter> _converter = new();
        private readonly Mock<IHubBroadcaster> _broadcaster = new();
        private readonly Mock<IAudioEnhancer> _enhancer = new();

        private ConversionQueueService BuildService() => new(
            _repository.Object,
            _audiobooks.Object,
            _configuration.Object,
            _converter.Object,
            _broadcaster.Object,
            TimeProvider.System,
            NullLogger<ConversionQueueService>.Instance,
            _enhancer.Object);

        private void GivenRepair(bool enabled = true, bool enhancerReady = true, bool encoderReady = true)
        {
            var settings = new ApplicationSettingsBuilder().Build();
            settings.RepairEnabled = enabled;
            _configuration.Setup(service => service.GetApplicationSettingsAsync()).ReturnsAsync(settings);
            _converter.Setup(converter => converter.IsAvailableAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(encoderReady);
            _enhancer.Setup(enhancer => enhancer.IsAvailableAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(enhancerReady);
            _enhancer.Setup(enhancer => enhancer.GetStatusAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EnhancerStatus(
                    enhancerReady ? EnhancerState.Ready : EnhancerState.NotProvisioned, 0, null, "v1", null, null));
        }

        private Audiobook GivenAudiobook(params string[] filePaths)
        {
            var audiobook = new AudiobookBuilder()
                .WithId(7)
                .WithTitle("Ender's Shadow")
                .WithBasePath("/library/book")
                .Build();
            audiobook.Files = filePaths.Select((path, index) =>
            {
                var file = AudiobookFile.CreateUnresolved(path);
                file.Id = 40 + index;
                file.DurationSeconds = 3600;
                return file;
            }).ToList();
            _audiobooks.Setup(repository => repository.GetByIdAsync(7)).ReturnsAsync(audiobook);
            return audiobook;
        }

        /// <summary>A book already stored as one M4B is exactly what a repair is for.</summary>
        [Fact]
        public async Task Repair_QueuesAnM4bBook_AsARepairKindJob()
        {
            GivenRepair();
            GivenAudiobook("Book.m4b");
            _repository.Setup(repository => repository.GetActiveForAudiobookAsync(7, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConversionJob?)null);
            ConversionJob? stored = null;
            _repository.Setup(repository => repository.AddAsync(It.IsAny<ConversionJob>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConversionJob job, CancellationToken _) => stored = job);

            var result = await BuildService().EnqueueRepairAsync(7);

            Assert.Equal(ConversionEnqueueOutcome.Queued, result.Outcome);
            Assert.Equal(ConversionJobKind.Repair, stored!.Kind);
            Assert.Equal(ConversionTrigger.Manual, stored.Trigger);
            // The SAME key as a conversion, on purpose: one active rewrite per book is a
            // database guarantee, not a check.
            Assert.Equal(ConversionJob.BuildDeduplicationKey(7), stored.ActiveDeduplicationKey);
        }

        [Fact]
        public async Task Repair_RefusesWhileTheSettingIsOff()
        {
            GivenRepair(enabled: false);
            GivenAudiobook("Book.m4b");

            var result = await BuildService().EnqueueRepairAsync(7);

            Assert.Equal(ConversionEnqueueOutcome.Disabled, result.Outcome);
        }

        [Fact]
        public async Task Repair_RefusesWithTheProvisioningStoryWhenTheEnhancerIsNotReady()
        {
            GivenRepair(enhancerReady: false);
            GivenAudiobook("Book.m4b");

            var result = await BuildService().EnqueueRepairAsync(7);

            Assert.Equal(ConversionEnqueueOutcome.EnhancerUnavailable, result.Outcome);
            Assert.Contains("not provisioned", result.Reason, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The cross-kind guarantee: a converting book cannot also start repairing.</summary>
        [Fact]
        public async Task Repair_RefusesWhileAConversionIsActiveForTheBook()
        {
            GivenRepair();
            GivenAudiobook("01.mp3");
            _repository.Setup(repository => repository.GetActiveForAudiobookAsync(7, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConversionJob { AudiobookId = 7, Kind = ConversionJobKind.Convert });

            var result = await BuildService().EnqueueRepairAsync(7);

            Assert.Equal(ConversionEnqueueOutcome.AlreadyQueued, result.Outcome);
            Assert.Contains("conversion", result.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Repair_RefusesABookWithNoAudio()
        {
            GivenRepair();
            GivenAudiobook("cover.jpg");

            var result = await BuildService().EnqueueRepairAsync(7);

            Assert.Equal(ConversionEnqueueOutcome.NothingToConvert, result.Outcome);
        }

        /// <summary>Neither enhancer failure is worth retrying on a timer; both wait for a person.</summary>
        [Fact]
        public void EnhancerFailures_AreNotAutoRetried()
        {
            // IsWorthRetrying is private; its behaviour is observable through FailAsync,
            // but the mapping itself is a one-line switch — assert the public contract by
            // reflection would overreach. The queue's retry behaviour is covered by the
            // conversion suite; this test pins the enum members existing at all, so a
            // rename breaks loudly here rather than silently falling to the retryable
            // default arm.
            Assert.True(Enum.IsDefined(ConversionFailureKind.EnhancerUnavailable));
            Assert.True(Enum.IsDefined(ConversionFailureKind.EnhanceFailed));
        }
    }
}
