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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Tagging
{
    /// <summary>
    /// What gets queued for tag writing, and what is refused with a reason rather than
    /// silently dropped.
    /// </summary>
    [Trait("Name", "TagQueueServiceTests")]
    [Trait("Category", "Tagging")]
    public sealed class TagQueueServiceTests : BaseTests
    {
        private readonly Mock<ITagJobRepository> _repository = new();
        private readonly Mock<IAudiobookRepository> _audiobooks = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IAudiobookTagWriter> _writer = new();
        private readonly Mock<IHubBroadcaster> _broadcaster = new();

        private TagQueueService BuildService() => new(
            _repository.Object,
            _audiobooks.Object,
            _configuration.Object,
            _writer.Object,
            _broadcaster.Object,
            TimeProvider.System,
            NullLogger<TagQueueService>.Instance);

        private void GivenSettings(bool automaticTagging) =>
            _configuration
                .Setup(service => service.GetApplicationSettingsAsync())
                .ReturnsAsync(automaticTagging
                    ? new ApplicationSettingsBuilder().WithAutomaticTagWriting().Build()
                    : new ApplicationSettingsBuilder().WithoutAutomaticTagWriting().Build());

        private void GivenWriterAvailable(bool available = true) =>
            _writer
                .Setup(writer => writer.IsAvailableAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(available);

        private Audiobook GivenAudiobook(params string[] filePaths)
        {
            var audiobook = new AudiobookBuilder()
                .WithId(7)
                .WithTitle("A Book")
                .WithBasePath("/library/book")
                .Build();

            audiobook.Files = [.. filePaths.Select(AudiobookFile.CreateUnresolved)];

            _audiobooks.Setup(repository => repository.GetByIdAsync(7)).ReturnsAsync(audiobook);
            return audiobook;
        }

        private void GivenNoActiveJob() =>
            _repository
                .Setup(repository => repository.GetActiveForAudiobookAsync(
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((TagJob?)null);

        private void GivenAddSucceeds() =>
            _repository
                .Setup(repository => repository.AddAsync(
                    It.IsAny<TagJob>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((TagJob job, CancellationToken _) => job);

        // ---- what gets queued -------------------------------------------------------

        [Fact]
        public async Task EnqueueAsync_QueuesAnM4bBook_WhenAutomaticTaggingIsOn()
        {
            GivenSettings(automaticTagging: true);
            GivenWriterAvailable();
            GivenAudiobook("Book.m4b");
            GivenNoActiveJob();
            GivenAddSucceeds();

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Automatic);

            Assert.Equal(TagEnqueueOutcome.Queued, result.Outcome);
        }

        [Fact]
        public async Task EnqueueAsync_RefusesAnMp3Book()
        {
            // ID3 cannot carry the desc atom this exists to write. Those books reach a
            // writable state by being converted first.
            GivenSettings(automaticTagging: true);
            GivenWriterAvailable();
            GivenAudiobook("Chapter 1.mp3", "Chapter 2.mp3");
            GivenNoActiveJob();

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Automatic);

            Assert.Equal(TagEnqueueOutcome.NothingToTag, result.Outcome);
            Assert.NotNull(result.Reason);
        }

        [Fact]
        public async Task EnqueueAsync_RefusesWhenAutomaticTaggingIsOff()
        {
            GivenSettings(automaticTagging: false);
            GivenAudiobook("Book.m4b");

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Automatic);

            Assert.Equal(TagEnqueueOutcome.Disabled, result.Outcome);
        }

        [Fact]
        public async Task EnqueueAsync_ManualRequestIgnoresTheSetting()
        {
            // A manual request is an explicit instruction; the setting only governs
            // whether an import queues one on its own.
            GivenSettings(automaticTagging: false);
            GivenWriterAvailable();
            GivenAudiobook("Book.m4b");
            GivenNoActiveJob();
            GivenAddSucceeds();

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Manual);

            Assert.Equal(TagEnqueueOutcome.Queued, result.Outcome);
        }

        [Fact]
        public async Task EnqueueAsync_RefusesWithoutAWriter_RatherThanQueueingAJobThatWillFail()
        {
            GivenSettings(automaticTagging: true);
            GivenWriterAvailable(false);
            GivenAudiobook("Book.m4b");
            GivenNoActiveJob();

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Manual);

            Assert.Equal(TagEnqueueOutcome.WriterUnavailable, result.Outcome);
            _repository.Verify(
                repository => repository.AddAsync(It.IsAny<TagJob>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task EnqueueAsync_RefusesAMissingBook()
        {
            _audiobooks.Setup(repository => repository.GetByIdAsync(7)).ReturnsAsync((Audiobook?)null);

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Manual);

            Assert.Equal(TagEnqueueOutcome.NotFound, result.Outcome);
        }

        // ---- one run per book --------------------------------------------------------

        [Fact]
        public async Task EnqueueAsync_ReturnsTheExistingJob_WhenOneIsAlreadyActive()
        {
            GivenSettings(automaticTagging: true);
            GivenWriterAvailable();
            GivenAudiobook("Book.m4b");

            var active = new TagJob { Id = Guid.NewGuid(), AudiobookId = 7 };
            _repository
                .Setup(repository => repository.GetActiveForAudiobookAsync(7, It.IsAny<CancellationToken>()))
                .ReturnsAsync(active);

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Manual);

            Assert.Equal(TagEnqueueOutcome.AlreadyQueued, result.Outcome);
            Assert.Equal(active.Id, result.JobId);
        }

        [Fact]
        public async Task EnqueueAsync_TreatsARejectedInsertAsAlreadyQueued()
        {
            // The unique index rejecting the row means a concurrent caller won the race,
            // which is the same outcome as finding an existing job. Two workers rewriting
            // one file is what that index exists to prevent.
            GivenSettings(automaticTagging: true);
            GivenWriterAvailable();
            GivenAudiobook("Book.m4b");
            GivenNoActiveJob();
            _repository
                .Setup(repository => repository.AddAsync(It.IsAny<TagJob>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((TagJob?)null);

            var result = await BuildService().EnqueueAsync(7, TagTrigger.Manual);

            Assert.Equal(TagEnqueueOutcome.AlreadyQueued, result.Outcome);
        }

        // ---- the per-run tag selection ------------------------------------------------

        [Fact]
        public async Task EnqueueAsync_RecordsTheOperatorsTagSelection()
        {
            GivenSettings(automaticTagging: true);
            GivenWriterAvailable();
            GivenAudiobook("Book.m4b");
            GivenNoActiveJob();

            TagJob? stored = null;
            _repository
                .Setup(repository => repository.AddAsync(It.IsAny<TagJob>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((TagJob job, CancellationToken _) =>
                {
                    stored = job;
                    return job;
                });

            await BuildService().EnqueueAsync(
                7,
                TagTrigger.Manual,
                [TagCatalog.Description, TagCatalog.Album]);

            Assert.NotNull(stored);
            var selection = TagQueueService.DeserializeSelection(stored!.SelectedTagsJson);
            Assert.NotNull(selection);
            Assert.Contains(TagCatalog.Description, selection!);
            Assert.Contains(TagCatalog.Album, selection!);
            Assert.DoesNotContain(TagCatalog.Artist, selection!);
        }

        [Fact]
        public async Task EnqueueAsync_WithNoSelection_MeansEveryTagTheMappingAllows()
        {
            GivenSettings(automaticTagging: true);
            GivenWriterAvailable();
            GivenAudiobook("Book.m4b");
            GivenNoActiveJob();

            TagJob? stored = null;
            _repository
                .Setup(repository => repository.AddAsync(It.IsAny<TagJob>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((TagJob job, CancellationToken _) =>
                {
                    stored = job;
                    return job;
                });

            await BuildService().EnqueueAsync(7, TagTrigger.Automatic, selectedTags: null);

            Assert.Null(stored!.SelectedTagsJson);
            Assert.Null(TagQueueService.DeserializeSelection(stored.SelectedTagsJson));
        }

        [Fact]
        public async Task EnqueueAsync_RecordsTheValuesTheOperatorTyped()
        {
            GivenSettings(automaticTagging: true);
            GivenWriterAvailable();
            GivenAudiobook("Book.m4b");
            GivenNoActiveJob();

            TagJob? stored = null;
            _repository
                .Setup(repository => repository.AddAsync(It.IsAny<TagJob>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((TagJob job, CancellationToken _) =>
                {
                    stored = job;
                    return job;
                });

            await BuildService().EnqueueAsync(
                7,
                TagTrigger.Manual,
                [TagCatalog.Album],
                new Dictionary<string, string> { [TagCatalog.Album] = "[The Expanse 0.5] Drive" });

            Assert.NotNull(stored);
            var values = TagQueueService.DeserializeValues(stored!.OverriddenValuesJson);
            Assert.NotNull(values);
            Assert.Equal("[The Expanse 0.5] Drive", values![TagCatalog.Album]);
        }

        [Fact]
        public void SerializeValues_DropsTagsThatDoNotExist()
        {
            var json = TagQueueService.SerializeValues(new Dictionary<string, string>
            {
                ["something_invented"] = "nonsense",
                [TagCatalog.Album] = "Drive"
            });

            var values = TagQueueService.DeserializeValues(json);
            Assert.NotNull(values);
            Assert.Single(values!);
            Assert.Equal("Drive", values![TagCatalog.Album]);
        }

        [Fact]
        public void SerializeValues_SanitisesBeforeAnythingReachesAnAtom()
        {
            // A value typed into a form is untrusted input, and a stray control
            // character would corrupt the atom carrying it.
            var json = TagQueueService.SerializeValues(new Dictionary<string, string>
            {
                [TagCatalog.Album] = "  Drive\u0000  "
            });

            var values = TagQueueService.DeserializeValues(json);
            Assert.Equal("Drive", values![TagCatalog.Album]);
        }

        [Fact]
        public void SerializeValues_OfBlanksOnly_StoresNothing()
        {
            // Nothing here ever writes an empty value, so a cleared box falls back to
            // the pattern rather than being read as "delete this tag".
            Assert.Null(TagQueueService.SerializeValues(new Dictionary<string, string>
            {
                [TagCatalog.Album] = "   "
            }));
        }

        [Fact]
        public void DeserializeValues_OfUnreadableJson_WritesNoOverriddenValueAtAll()
        {
            // Falling back to the patterns the operator deliberately overrode would
            // write the values they had just corrected.
            var values = TagQueueService.DeserializeValues("{not json");

            Assert.NotNull(values);
            Assert.Empty(values!);
        }

        [Fact]
        public void DeserializeSelection_OfUnreadableJson_WritesNothingRatherThanEverything()
        {
            // A selection that cannot be read is not a reason to write every field the
            // operator may have deliberately excluded.
            var selection = TagQueueService.DeserializeSelection("{not json");

            Assert.NotNull(selection);
            Assert.Empty(selection!);
        }

        [Fact]
        public void SerializeSelection_DropsTagsThatDoNotExist()
        {
            var json = TagQueueService.SerializeSelection(["something_invented", TagCatalog.Album]);

            var selection = TagQueueService.DeserializeSelection(json);
            Assert.NotNull(selection);
            Assert.Single(selection!);
            Assert.Contains(TagCatalog.Album, selection!);
        }
        [Fact]
        [Trait("Method", "DismissAsync")]
        public async Task DismissAsync_RefusesAJobHoldingTheBooksOnlyCopy()
        {
            // Past the point of removing the original, this row is the only record of
            // where the replacement is. Deleting it would hand the book's only file to
            // the next scratch sweep, so the refusal is the safety property, not a
            // convenience.
            var job = new TagJob
            {
                Id = Guid.NewGuid(),
                AudiobookId = 7,
                Status = TagJobStatus.Failed,
                PendingOutputPath = "/scratch/tagging-abc-593.m4b",
                PendingDestinationPath = "/audiobooks/Book/Book.m4b",
                PendingFileId = 593
            };
            _repository
                .Setup(repo => repo.GetAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(job);

            var result = await BuildService().DismissAsync(job.Id);

            Assert.Equal(JobControlOutcome.HoldsOnlyCopy, result.Outcome);
            Assert.Contains("only copy", result.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            _repository.Verify(
                repo => repo.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        [Trait("Method", "CancelAsync")]
        public async Task CancelAsync_RefusesAJobHoldingTheBooksOnlyCopy()
        {
            var job = new TagJob
            {
                Id = Guid.NewGuid(),
                AudiobookId = 7,
                Status = TagJobStatus.Running,
                PendingOutputPath = "/scratch/tagging-abc-593.m4b"
            };
            _repository
                .Setup(repo => repo.GetAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(job);

            var result = await BuildService().CancelAsync(job.Id);

            Assert.Equal(JobControlOutcome.HoldsOnlyCopy, result.Outcome);
            _repository.Verify(
                repo => repo.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Action<TagJob>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        [Trait("Method", "DismissAsync")]
        public async Task DismissAsync_RemovesAFailedJobThatHoldsNothing()
        {
            var job = new TagJob
            {
                Id = Guid.NewGuid(),
                AudiobookId = 7,
                Status = TagJobStatus.Failed
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
        [Trait("Method", "CancelAsync")]
        public async Task CancelAsync_MovesARunningJobOffRunningSoItsWorkerStops()
        {
            var job = new TagJob
            {
                Id = Guid.NewGuid(),
                AudiobookId = 7,
                Status = TagJobStatus.Running,
                ActiveDeduplicationKey = TagJob.BuildDeduplicationKey(7)
            };
            _repository
                .Setup(repo => repo.GetAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(job);
            _repository
                .Setup(repo => repo.UpdateAsync(job.Id, It.IsAny<Action<TagJob>>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, Action<TagJob>, CancellationToken>((_, mutate, _) => mutate(job))
                .ReturnsAsync(true);

            var result = await BuildService().CancelAsync(job.Id);

            Assert.True(result.Succeeded);
            Assert.Equal(TagJobStatus.Cancelled, job.Status);
            Assert.Null(job.ActiveDeduplicationKey);
        }

    }
}
