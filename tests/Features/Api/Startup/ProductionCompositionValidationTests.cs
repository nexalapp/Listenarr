using Listenarr.Api.Startup;
using Listenarr.Application.Audiobooks.Discover;
using Listenarr.Application.Audiobooks.AutoGrab;
using Listenarr.Application.Audiobooks.Suggestions;
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Infrastructure.DependencyInjection;
using Listenarr.Infrastructure.Library.Discover.Workers;
using Listenarr.Infrastructure.Library.AutoGrab;
using Listenarr.Infrastructure.Library.Suggestions.Workers;
using Listenarr.Infrastructure.Library.Tagging;
using Listenarr.Domain.Audiobooks.Discover;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Startup;

[Trait("Name", "ProductionCompositionValidationTests")]
[Trait("Category", "Api")]
public sealed class ProductionCompositionValidationTests : BaseTests
{
    [Fact]
    public void DevelopmentComposition_ValidatesCompleteProductionServiceGraph()
    {
        var contentRoot = Path.Join(
            Path.GetTempPath(),
            "listenarr-tests",
            $"development-composition-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = contentRoot,
                EnvironmentName = Environments.Development
            });
            var fileSystem = new LocalFileSystem();
            builder.Configuration["Listenarr:SqliteDbPath"] = Path.Join(
                contentRoot,
                "composition.db");
            builder.AddListenarrApiServices(fileSystem);
            builder.Services.AddListenarrInfrastructureComposition(
                builder.Configuration,
                builder.Environment);

            Type[] affectedSingletonServiceTypes =
            [
                typeof(TimeProvider),
                typeof(IFilesystemMutationCoordinator),
                typeof(IDirectoryObjectIdentityResolver),
                typeof(IRootFolderStorageHealthResolver),
                typeof(LibraryDirectoryOwnershipBoundaryAuthorizer),
                typeof(IAudiobookOperationCoordinator),
                typeof(IAudiobookUpdatePublisher),
                typeof(IRootFolderRelocationService),
                typeof(IMoveCleanupBoundaryResolver),
                typeof(ILibraryDirectoryOwnershipStore),
                typeof(IAudiobookDeletionIntentProbe),
                typeof(IFileRegistrationRecoveryProbe),
                typeof(IFileRenameRecoveryProbe),
                typeof(IMoveQueueService),
                typeof(IMoveQueuePersistence),
                typeof(IMoveExecutionStore),
                typeof(IMoveScanHandoffStore),
                typeof(LibraryFilesystemReadiness),
                typeof(ILibraryFilesystemReadiness),
                typeof(ILibraryFilesystemMutationGate),
                typeof(IFileSystemSemanticsResolver),
                typeof(IFileSystem),
                typeof(IStartupConfigService),
                typeof(IFfmpegService),
                typeof(IScanQueueService),
                typeof(IUnmatchedScanQueueService),
                typeof(MoveScanHandoffRecoveryService),
                typeof(ScanJobProcessor),
                typeof(IScanJobProcessor),
                typeof(AudiobookContentMoveService),
                typeof(MoveJobProcessor),
                typeof(IMoveJobProcessor),
                typeof(UnmatchedScanProcessor),
                typeof(IUnmatchedScanProcessor),
                typeof(MetadataRescanService),
                typeof(DownloadProcessingJobProcessor),
                typeof(IDownloadImportProcessor),
                // The tag worker holds a scope factory and nothing scoped, the same
                // shape as the conversion worker. A scoped capture here would be a
                // DbContext shared across every rewrite the process ever runs.
                typeof(TagJobProcessor),
                typeof(ITagJobProcessor),
                typeof(TagBackgroundService),
                typeof(UnmatchedScanBackgroundService),
                // The auto-grab worker holds a scope factory and the clock, and opens
                // its own scope per run: it adds books, so a captured DbContext here
                // would be a long-lived tracker across every add the process makes.
                typeof(AutoGrabProcessor),
                typeof(IAutoGrabProcessor),
                typeof(AutoGrabBackgroundService),
                typeof(ReleasePreparationProcessor),
                typeof(IReleasePreparationProcessor),
                typeof(ReleasePreparationBackgroundService),
                // The discovery refresher keeps the progress the page polls and opens
                // its own scope per page fetch; its worker holds a scope factory and
                // the refresher, and nothing scoped.
                typeof(IDiscoverRefreshService),
                typeof(DiscoverSectionCatalogCache),
                typeof(DiscoverFetchProcessor),
                typeof(IDiscoverFetchProcessor),
                typeof(DiscoverBackgroundService)
            ];
            foreach (var serviceType in affectedSingletonServiceTypes)
            {
                var descriptor = Assert.Single(
                    builder.Services,
                    candidate => candidate.ServiceType == serviceType);
                Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            }

            Type[] affectedScopedServiceTypes =
            [
                typeof(IAudiobookDeletionCommitService),
                typeof(IAudiobookDeletionIntentStore),
                typeof(IAudiobookDeletionIntentReconciler),
                typeof(IFileRenameCommitStore),
                typeof(IFileRegistrationRecoveryService),
                typeof(IFileRenameRecoveryReconciler),
                typeof(IAudiobookFileIdentityReconciler),
                typeof(IAudiobookFilesystemDeleteService),
                typeof(IRenameService),
                typeof(IRootFolderStorageConfirmationService),
                // Everything on the tag-writing path holds a DbContext through a
                // repository or the configuration service, directly or transitively.
                typeof(ITagJobRepository),
                typeof(ITagQueueService),
                typeof(IAudiobookTagWriter),
                typeof(ITagPreviewService),
                typeof(AudiobookTagPlanner),
                typeof(IAutoGrabService),
                typeof(IReleasePreparationService),
                // Everything on the discovery read path holds a DbContext through a
                // repository or the configuration service.
                typeof(IDiscoverService),
                typeof(IDiscoverRepository),
                typeof(IDiscoverMetadataResolver)
            ];
            foreach (var serviceType in affectedScopedServiceTypes)
            {
                var descriptor = Assert.Single(
                    builder.Services,
                    candidate => candidate.ServiceType == serviceType);
                Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            }

            Type[] affectedHostedServiceTypes =
            [
                typeof(LibraryFilesystemStartupReconciliationService),
                typeof(ScanBackgroundService),
                typeof(MoveBackgroundService),
                typeof(MetadataRescanService),
                typeof(DownloadProcessingJobProcessor),
                typeof(UnmatchedScanBackgroundService),
                typeof(TagBackgroundService),
                typeof(StartupDbNormalizer),
                typeof(AutoGrabBackgroundService),
                typeof(DiscoverBackgroundService)
            ];
            Assert.All(
                builder.Services.Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)),
                descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));

            using var provider = builder.Services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                });

            foreach (var serviceType in affectedSingletonServiceTypes)
            {
                Assert.NotNull(provider.GetRequiredService(serviceType));
            }
            using (var scope = provider.CreateScope())
            {
                foreach (var serviceType in affectedScopedServiceTypes)
                {
                    Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
                }

                // One reader per discovery source, resolved as a set rather than by
                // name, so a missing registration shows up here rather than as an
                // empty tab.
                var readers = scope.ServiceProvider.GetServices<IDiscoverSourceReader>().ToList();
                Assert.Equal(
                    [DiscoverSources.AbookLink, DiscoverSources.AudibleCharts],
                    readers.Select(reader => reader.Source).OrderBy(source => source, StringComparer.Ordinal));
            }

            Assert.All(
                builder.Services.Where(descriptor => descriptor.ServiceType == typeof(IDiscoverSourceReader)),
                descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));

            var hostedServices = provider.GetServices<IHostedService>().ToList();
            foreach (var implementationType in affectedHostedServiceTypes)
            {
                Assert.Single(hostedServices, service =>
                    service.GetType() == implementationType);
            }

            Assert.Same(
                provider.GetRequiredService<ScanJobProcessor>(),
                provider.GetRequiredService<IScanJobProcessor>());
            Assert.Same(
                provider.GetRequiredService<MoveJobProcessor>(),
                provider.GetRequiredService<IMoveJobProcessor>());
            Assert.Same(
                provider.GetRequiredService<UnmatchedScanProcessor>(),
                provider.GetRequiredService<IUnmatchedScanProcessor>());
            Assert.Same(
                provider.GetRequiredService<LibraryFilesystemReadiness>(),
                provider.GetRequiredService<ILibraryFilesystemReadiness>());
            Assert.Same(
                provider.GetRequiredService<LibraryFilesystemReadiness>(),
                provider.GetRequiredService<ILibraryFilesystemMutationGate>());
            Assert.Same(
                provider.GetRequiredService<DownloadProcessingJobProcessor>(),
                provider.GetRequiredService<IDownloadImportProcessor>());
            Assert.Same(
                provider.GetRequiredService<MetadataRescanService>(),
                Assert.Single(hostedServices.OfType<MetadataRescanService>()));
            Assert.Same(
                provider.GetRequiredService<UnmatchedScanBackgroundService>(),
                Assert.Single(hostedServices.OfType<UnmatchedScanBackgroundService>()));
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }
}
