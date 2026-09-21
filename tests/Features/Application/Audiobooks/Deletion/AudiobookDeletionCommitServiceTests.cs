using Listenarr.Application.Audiobooks;
using Listenarr.Application.Audiobooks.Conversion;
using Listenarr.Application.Audiobooks.Deletion;
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Domain.Audiobooks.Tagging;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Deletion;

[Trait("Area", "Library")]
[Trait("Name", "AudiobookDeletionCommitServiceTests")]
[Trait("Category", "Application")]
public sealed class AudiobookDeletionCommitServiceTests : BaseTests
{
    [Fact]
    public async Task DeleteAsync_RequestCanceledWhilePreflightCompletes_DoesNotCommit()
    {
        // Given
        const int audiobookId = 4101;
        var audiobook = new Audiobook
        {
            Id = audiobookId,
            Title = "Cancelable delete"
        };
        var preflightStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePreflight = new TaskCompletionSource<Audiobook?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<IAudiobookRepository>(MockBehavior.Strict);
        repository.Setup(service => service.GetForUpdateSnapshotAsync(
                audiobookId,
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                preflightStarted.SetResult();
                return await releasePreflight.Task;
            });
        using var cancellation = new CancellationTokenSource();
        var service = Build(repository.Object);

        // When
        var deletion = service.DeleteAsync(audiobookId, cancellation.Token);
        await preflightStarted.Task;
        cancellation.Cancel();
        releasePreflight.SetResult(audiobook);

        // Then
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => deletion);
        repository.Verify(service => service.DeleteByIdAsync(audiobookId), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_RequestCanceledAfterCommitBoundary_CompletesCommit()
    {
        // Given
        const int audiobookId = 4102;
        var audiobook = new Audiobook
        {
            Id = audiobookId,
            Title = "Committed delete"
        };
        using var cancellation = new CancellationTokenSource();
        var repository = new Mock<IAudiobookRepository>(MockBehavior.Strict);
        repository.Setup(service => service.GetForUpdateSnapshotAsync(
                audiobookId,
                cancellation.Token))
            .ReturnsAsync(audiobook);
        repository.Setup(service => service.DeleteByIdAsync(audiobookId))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult(true);
            });
        var service = Build(repository.Object);

        // When
        var result = await service.DeleteAsync(audiobookId, cancellation.Token);

        // Then
        Assert.Equal(AudiobookDeletionCommitOutcome.Deleted, result.Outcome);
        Assert.Same(audiobook, result.Audiobook);
        Assert.True(cancellation.IsCancellationRequested);
        repository.Verify(service => service.DeleteByIdAsync(audiobookId), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_IncludeFiles_UsesFullSnapshotForPostCommitFilesystemCleanup()
    {
        // Given
        const int audiobookId = 4104;
        var audiobook = new Audiobook
        {
            Id = audiobookId,
            Title = "Filesystem delete",
            Files = [AudiobookFile.CreateUnresolved("/library/book.m4b")]
        };
        var repository = new Mock<IAudiobookRepository>(MockBehavior.Strict);
        repository.Setup(service => service.GetForUpdateSnapshotAsync(
                audiobookId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Audiobook
            {
                Id = audiobookId,
                Title = audiobook.Title
            });
        repository.Setup(service => service.GetByIdSnapshotAsync(
                audiobookId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(audiobook);
        repository.Setup(service => service.DeleteByIdAsync(audiobookId))
            .ReturnsAsync(true);
        var service = Build(repository.Object);

        // When
        var result = await service.DeleteAsync(
            audiobookId,
            includeFiles: true,
            CancellationToken.None);

        // Then
        Assert.Equal(AudiobookDeletionCommitOutcome.Deleted, result.Outcome);
        Assert.Same(audiobook, result.Audiobook);
        repository.Verify(service => service.GetForUpdateSnapshotAsync(
            audiobookId,
            It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(service => service.GetByIdSnapshotAsync(
            audiobookId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_IncludeFilesWithForeignHostBoundary_DoesNotLoadFileGraph()
    {
        // Given
        const int audiobookId = 4105;
        var foreignBasePath = OperatingSystem.IsWindows()
            ? "/server/mnt/drive/Audiobooks/Imported"
            : @"C:\server\Audiobooks\Imported";
        var audiobook = new Audiobook
        {
            Id = audiobookId,
            Title = "Copied database",
            BasePath = foreignBasePath
        };
        var repository = new Mock<IAudiobookRepository>(MockBehavior.Strict);
        repository.Setup(service => service.GetForUpdateSnapshotAsync(
                audiobookId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(audiobook);
        repository.Setup(service => service.DeleteByIdAsync(audiobookId))
            .ReturnsAsync(true);
        var service = Build(repository.Object);

        // When
        var result = await service.DeleteAsync(
            audiobookId,
            includeFiles: true,
            CancellationToken.None);

        // Then
        Assert.Equal(AudiobookDeletionCommitOutcome.Deleted, result.Outcome);
        Assert.Same(audiobook, result.Audiobook);
        repository.Verify(service => service.GetByIdSnapshotAsync(
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_CanceledBeforePreflight_DoesNotCommit()
    {
        // Given
        const int audiobookId = 4103;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new Mock<IAudiobookRepository>(MockBehavior.Strict);
        var service = Build(repository.Object);

        // When / Then
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DeleteAsync(audiobookId, cancellation.Token));
        repository.Verify(service => service.GetForUpdateSnapshotAsync(
            audiobookId,
            It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(service => service.DeleteByIdAsync(audiobookId), Times.Never);
    }

    private readonly Mock<IConversionQueueService> _conversions = new();
    private readonly Mock<ITagQueueService> _tags = new();
    private readonly Mock<IConversionJobRepository> _conversionJobs = new();
    private readonly Mock<ITagJobRepository> _tagJobs = new();

    private AudiobookDeletionCommitService Build(IAudiobookRepository repository) =>
        new(
            repository,
            _conversions.Object,
            _tags.Object,
            _conversionJobs.Object,
            _tagJobs.Object,
            NullLogger<AudiobookDeletionCommitService>.Instance);

    [Fact]
    public async Task DeleteAsync_StopsAJobStillWorkingOnTheBook_BeforeTheBookGoes()
    {
        // A conversion encoding the book while it is deleted would go on for minutes
        // and then publish into a folder that no longer exists; its row would vanish
        // from under it when the key cascaded. It is cancelled first.
        const int audiobookId = 4103;
        var audiobook = new Audiobook { Id = audiobookId, Title = "Mid-encode" };
        var repository = new Mock<IAudiobookRepository>();
        repository.Setup(r => r.GetForUpdateSnapshotAsync(audiobookId, It.IsAny<CancellationToken>())).ReturnsAsync(audiobook);
        repository.Setup(r => r.DeleteByIdAsync(audiobookId)).ReturnsAsync(true);
        var conversion = new ConversionJob { AudiobookId = audiobookId };
        var tagging = new TagJob { AudiobookId = audiobookId };
        _conversions.Setup(q => q.GetActiveJobForAudiobookAsync(audiobookId, It.IsAny<CancellationToken>())).ReturnsAsync(conversion);
        _conversions.Setup(q => q.CancelAsync(conversion.Id, It.IsAny<CancellationToken>())).ReturnsAsync(JobControlResult.Done());
        _tags.Setup(q => q.GetActiveJobForAudiobookAsync(audiobookId, It.IsAny<CancellationToken>())).ReturnsAsync(tagging);
        _tags.Setup(q => q.CancelAsync(tagging.Id, It.IsAny<CancellationToken>())).ReturnsAsync(JobControlResult.Done());

        var result = await Build(repository.Object).DeleteAsync(audiobookId);

        Assert.Equal(AudiobookDeletionCommitOutcome.Deleted, result.Outcome);
        _conversions.Verify(q => q.CancelAsync(conversion.Id, It.IsAny<CancellationToken>()), Times.Once);
        _tags.Verify(q => q.CancelAsync(tagging.Id, It.IsAny<CancellationToken>()), Times.Once);
        // And the rows go with the book, terminal ones included.
        _conversionJobs.Verify(r => r.DeleteForAudiobookAsync(audiobookId, It.IsAny<CancellationToken>()), Times.Once);
        _tagJobs.Verify(r => r.DeleteForAudiobookAsync(audiobookId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ThatDidNotCommit_LeavesTheJobsAlone()
    {
        const int audiobookId = 4104;
        var repository = new Mock<IAudiobookRepository>();
        repository.Setup(r => r.GetForUpdateSnapshotAsync(audiobookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Audiobook { Id = audiobookId, Title = "Stays" });
        repository.Setup(r => r.DeleteByIdAsync(audiobookId)).ReturnsAsync(false);

        var result = await Build(repository.Object).DeleteAsync(audiobookId);

        Assert.Equal(AudiobookDeletionCommitOutcome.Failed, result.Outcome);
        _conversionJobs.Verify(r => r.DeleteForAudiobookAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _tagJobs.Verify(r => r.DeleteForAudiobookAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
