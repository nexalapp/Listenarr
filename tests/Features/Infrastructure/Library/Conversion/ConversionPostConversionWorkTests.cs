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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Infrastructure.Library.Conversion;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Conversion
{
    /// <summary>
    /// A download that arrives as parts is converted instead of checked, so the import's
    /// checks have to run once the conversion is done — with the import's trigger, or the
    /// probe's verdict is shown and a codec-damaged book is never offered for repair.
    /// </summary>
    [Trait("Name", "ConversionPostConversionWorkTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class ConversionPostConversionWorkTests : BaseTests
    {
        private readonly Mock<IAudioQualityService> _prober = new();
        private readonly Mock<IAudioAuditService> _auditor = new();
        private readonly Mock<ILibraryTagIndexService> _index = new();

        private IServiceProvider Services()
        {
            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(IAudioQualityService))).Returns(_prober.Object);
            provider.Setup(p => p.GetService(typeof(IAudioAuditService))).Returns(_auditor.Object);
            provider.Setup(p => p.GetService(typeof(ILibraryTagIndexService))).Returns(_index.Object);
            return provider.Object;
        }

        private static ConversionJobProcessor Processor() => new(
            new Mock<IServiceScopeFactory>().Object,
            NullLogger<ConversionJobProcessor>.Instance);

        private static ConversionJob AJob(ConversionJobKind kind, ConversionTrigger trigger) => new()
        {
            Id = Guid.NewGuid(),
            AudiobookId = 4064,
            Kind = kind,
            Trigger = trigger,
            Status = ConversionJobStatus.Completed
        };

        [Fact]
        public async Task AnImportsConversion_IsProbedAsAnImport_SoADamagedBookIsOfferedForRepair()
        {
            await Processor().QueuePostConversionWorkAsync(AJob(ConversionJobKind.Convert, ConversionTrigger.Automatic), Services());

            _prober.Verify(p => p.EnqueueAsync(4064, TagTrigger.Import, It.IsAny<CancellationToken>()), Times.Once);
            _auditor.Verify(a => a.EnqueueAsync(4064, TagTrigger.Automatic, It.IsAny<CancellationToken>(), false), Times.Once);
            _index.Verify(i => i.BuildAsync(false, It.IsAny<CancellationToken>(), It.Is<IReadOnlyCollection<int>>(ids => ids.Single() == 4064), TagTrigger.Import), Times.Once);
        }

        [Fact]
        public async Task AManualConversion_OfABookAlreadyInTheLibrary_QueuesNothing()
        {
            await Processor().QueuePostConversionWorkAsync(AJob(ConversionJobKind.Convert, ConversionTrigger.Manual), Services());

            _prober.VerifyNoOtherCalls();
            _auditor.VerifyNoOtherCalls();
            _index.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ARepair_IsLeftToItsOwnFollowUp()
        {
            await Processor().QueuePostConversionWorkAsync(AJob(ConversionJobKind.Repair, ConversionTrigger.Automatic), Services());

            _prober.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AFailingChapterVerdict_StillLeavesTheProbeQueued()
        {
            _index
                .Setup(i => i.BuildAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<TagTrigger>()))
                .ThrowsAsync(new InvalidOperationException("index unavailable"));

            await Processor().QueuePostConversionWorkAsync(AJob(ConversionJobKind.Convert, ConversionTrigger.Automatic), Services());

            _prober.Verify(p => p.EnqueueAsync(4064, TagTrigger.Import, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
