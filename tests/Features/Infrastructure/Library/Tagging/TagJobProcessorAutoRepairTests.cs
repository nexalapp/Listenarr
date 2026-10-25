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
using Listenarr.Application.Audiobooks.Conversion;
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Quality;
using Listenarr.Infrastructure.Library.Tagging;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Tagging
{
    /// <summary>
    /// Auto-repair: an import's own probe or chapter planning queues the repair when its
    /// setting is on; the same finding anywhere else only shows.
    /// </summary>
    [Trait("Name", "TagJobProcessorAutoRepairTests")]
    [Trait("Category", "Tagging")]
    public sealed class TagJobProcessorAutoRepairTests : BaseTests
    {
        private readonly Mock<ITagQueueService> _queue = new();
        private readonly Mock<IChapterRepairService> _chapters = new();
        private readonly Mock<IAudioQualityService> _prober = new();
        private readonly Mock<IConversionQueueService> _conversion = new();
        private readonly ApplicationSettings _settings = new();
        private readonly Queue<TagJob> _pending = new();

        private TagJobProcessor BuildProcessor()
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
            var configuration = new Mock<IConfigurationService>();
            configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(_settings);

            _queue.Setup(q => q.RecoverAbandonedJobsAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _queue
                .Setup(q => q.ClaimNextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _pending.Count > 0 ? _pending.Dequeue() : null);
            _queue.Setup(q => q.HeartbeatAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _queue
                .Setup(q => q.ReportProgressAsync(It.IsAny<Guid>(), It.IsAny<TagJobPhase>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _queue.Setup(q => q.CompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            _chapters
                .Setup(r => r.EnqueueAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TagEnqueueResult(TagEnqueueOutcome.Queued, Guid.NewGuid()));
            _conversion
                .Setup(c => c.EnqueueRepairAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConversionEnqueueResult(ConversionEnqueueOutcome.Queued, Guid.NewGuid()));

            var services = new ServiceCollection();
            services.AddSingleton(_queue.Object);
            services.AddSingleton(_chapters.Object);
            services.AddSingleton(_prober.Object);
            services.AddSingleton(_conversion.Object);
            services.AddSingleton(configuration.Object);
            services.AddSingleton(audiobooks.Object);
            services.AddSingleton(moves.Object);
            services.AddSingleton(paths.Object);
            services.AddSingleton(new Mock<IToastService>().Object);
            var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            return new TagJobProcessor(scopes, NullLogger<TagJobProcessor>.Instance);
        }

        private void GivenPlanFinds(bool fix) =>
            _chapters
                .Setup(r => r.PlanAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, IReadOnlyCollection<int>? _, IProgress<double>? _, CancellationToken _) => new ChapterRepairPreview(id,
                [
                    new ChapterRepairFilePreview(
                        1, "Book.m4b", ChapterHealth.Corrupt, "corrupt",
                        fix ? new ChapterPlan(ChapterSource.Played, [], false, "") : null,
                        fix ? null : "no source")
                ]));

        private void GivenProbeRecommends(bool recommended) =>
            _prober
                .Setup(p => p.ProbeAsync(It.IsAny<int>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AudioQualityReport(
                    recommended ? AudioQuality.CodecDamaged : AudioQuality.Clean,
                    "reason",
                    new AudioQualityMetrics(-70, 2, 8000, -25, 0, null, 10),
                    recommended,
                    false));

        private void GivenJob(TagJobKind kind, TagTrigger trigger) =>
            _pending.Enqueue(new TagJob { AudiobookId = 9, Kind = kind, Trigger = trigger, FileCount = 1, SelectedFileIdsJson = "[1]" });

        [Fact]
        public async Task AnImportsPlanWithAFix_QueuesTheChapterRepair()
        {
            GivenPlanFinds(fix: true);
            GivenJob(TagJobKind.Plan, TagTrigger.Import);

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _chapters.Verify(r => r.EnqueueAsync(9, It.Is<IReadOnlyCollection<int>?>(ids => ids != null && ids.Single() == 1), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(TagTrigger.Automatic)]
        [InlineData(TagTrigger.Manual)]
        public async Task APlanFromAnywhereButAnImport_OnlyShows(TagTrigger trigger)
        {
            // The probe sweep, a table load and Check Chapters find needs across the existing
            // library; acting on those would be a bulk rewrite nobody asked for.
            GivenPlanFinds(fix: true);
            GivenJob(TagJobKind.Plan, trigger);

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _chapters.Verify(r => r.EnqueueAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AnImportsPlan_IsLeftAloneWhenTheSettingIsOff_OrThereIsNoFix()
        {
            _settings.AutoRepairChapters = false;
            GivenPlanFinds(fix: true);
            GivenJob(TagJobKind.Plan, TagTrigger.Import);
            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _settings.AutoRepairChapters = true;
            GivenPlanFinds(fix: false);
            GivenJob(TagJobKind.Plan, TagTrigger.Import);
            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _chapters.Verify(r => r.EnqueueAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AnImportsProbeRecommendingRepair_QueuesTheAudioRepair()
        {
            GivenProbeRecommends(true);
            GivenJob(TagJobKind.Probe, TagTrigger.Import);

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _conversion.Verify(c => c.EnqueueRepairAsync(9, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task TheSweepsProbe_NeverQueuesARepair()
        {
            GivenProbeRecommends(true);
            GivenJob(TagJobKind.Probe, TagTrigger.Automatic);

            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _conversion.Verify(c => c.EnqueueRepairAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AnImportsProbe_IsLeftAloneWhenTheSettingIsOff_OrNothingIsRecommended()
        {
            _settings.AutoRepairAudio = false;
            GivenProbeRecommends(true);
            GivenJob(TagJobKind.Probe, TagTrigger.Import);
            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _settings.AutoRepairAudio = true;
            GivenProbeRecommends(false);
            GivenJob(TagJobKind.Probe, TagTrigger.Import);
            await BuildProcessor().RunCycleAsync(CancellationToken.None);

            _conversion.Verify(c => c.EnqueueRepairAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ARefusedAutoRepair_DoesNotFailTheJobThatFoundTheNeed()
        {
            GivenProbeRecommends(true);
            _conversion
                .Setup(c => c.EnqueueRepairAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("database is locked"));
            var processor = BuildProcessor();
            _conversion
                .Setup(c => c.EnqueueRepairAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("database is locked"));
            GivenJob(TagJobKind.Probe, TagTrigger.Import);

            await processor.RunCycleAsync(CancellationToken.None);

            _queue.Verify(q => q.CompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
