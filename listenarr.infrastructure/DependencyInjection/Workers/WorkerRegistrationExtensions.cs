/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Infrastructure.FoundBooks.Workers;
using Listenarr.Infrastructure.Library.Suggestions.Workers;
using Listenarr.Infrastructure.Library.Discover.Workers;
using Listenarr.Infrastructure.Library.Quality;
using Listenarr.Infrastructure.Library.Series;
using Listenarr.Infrastructure.HostedServices;
using Listenarr.Infrastructure.Search.NzbKing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Listenarr.Infrastructure.DependencyInjection.Workers;

internal static class WorkerRegistrationExtensions
{
    public static IServiceCollection AddFeatureWorkers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IWorkerCycleRunner, WorkerCycleRunner>();

        services.AddSingleton<IScanQueueService, ScanQueueService>();
        services.AddSingleton<MoveScanHandoffRecoveryService>();
        AddProcessor<ScanJobProcessor, IScanJobProcessor>(services);
        services.AddHostedService<ScanBackgroundService>();
        services.AddHostedService<SeriesAuthorRefreshService>();

        services.AddSingleton<AudiobookContentMoveService>();
        AddProcessor<MoveJobProcessor, IMoveJobProcessor>(services);
        services.AddHostedService<MoveBackgroundService>();

        AddHostedProcessor<ImageCacheCleanupProcessor, IImageCacheCleanupProcessor, ImageCacheCleanupService>(services);
        AddHostedProcessor<DownloadMonitorProcessor, IDownloadMonitorProcessor, DownloadMonitorService>(services);
        AddHostedProcessor<DirectDownloadProcessor, IDirectDownloadProcessor, DirectDownloadService>(services);
        AddHostedProcessor<MovedDownloadCleanupProcessor, IMovedDownloadCleanupProcessor, MovedDownloadCleanupService>(services);

        AddProcessor<QueueMonitorProcessor, IQueueMonitorProcessor>(services);
        services.AddHostedService<QueueMonitorService>();

        AddHostedProcessor<AutomaticSearchProcessor, IAutomaticSearchProcessor, AutomaticSearchService>(services);
        AddHostedProcessor<AuthorMonitoringProcessor, IAuthorMonitoringProcessor, AuthorMonitoringBackgroundService>(services);
        AddHostedProcessor<NzbKingKeepaliveProcessor, INzbKingKeepaliveProcessor, NzbKingKeepaliveBackgroundService>(services);
        AddHostedProcessor<SeriesMonitoringProcessor, ISeriesMonitoringProcessor, SeriesMonitoringBackgroundService>(services);
        AddHostedProcessor<FfmpegInstallProcessor, IFfmpegInstallProcessor, FfmpegInstallBackgroundService>(services);
        AddHostedProcessor<MetadataRescanProcessor, IMetadataRescanProcessor, MetadataRescanService>(services);
        services.AddSingleton<DownloadProcessingJobProcessor>();
        services.AddSingleton<IDownloadImportProcessor>(provider =>
            provider.GetRequiredService<DownloadProcessingJobProcessor>());
        services.AddHostedService(provider =>
            provider.GetRequiredService<DownloadProcessingJobProcessor>());

        // Retention cleanup gets its own worker so importing files and pruning old
        // terminal processing-job rows remain separate durable responsibilities.
        AddHostedProcessor<
            DownloadProcessingJobCleanupProcessor,
            IDownloadProcessingJobCleanupProcessor,
            DownloadProcessingJobCleanupService>(services);

        // Conversion is deliberately its own worker rather than a stage of the import
        // processor: an encode can run for an hour, and sharing that worker would stall
        // every other import behind it.
        AddHostedProcessor<
            ConversionJobProcessor,
            IConversionJobProcessor,
            ConversionBackgroundService>(services);

        // Tag writing is its own worker for the same reason, and separate from conversion
        // besides: a book already in M4B never touches the conversion queue, and one that
        // does is tagged by the conversion itself.
        AddHostedProcessor<
            TagJobProcessor,
            ITagJobProcessor,
            TagBackgroundService>(services);

        // The one-time walk of the existing library through the audio-quality probe.
        // Its jobs run on the tag worker above; this only keeps a trickle of them queued.
        services.AddHostedService<AudioQualitySweepService>();

        // The repair lane: same queue as conversion, own serial worker, so a days-long
        // repair cannot park ordinary conversions behind it.
        services.AddHostedService<Listenarr.Infrastructure.Library.Repair.RepairBackgroundService>();

        AddHostedProcessor<UnmatchedScanProcessor, IUnmatchedScanProcessor, UnmatchedScanBackgroundService>(services);

        // Found books: a periodic read-only look at the download folders for complete
        // books the library does not have.
        AddHostedProcessor<
            FoundBookScanProcessor,
            IFoundBookScanProcessor,
            FoundBookScanBackgroundService>(services);
        // The imports a person queues from the Found tab; the row is the queue.
        services.AddHostedService<FoundBookImportBackgroundService>();

        // Suggested: one missing author or series catalog per interval, so the cache
        // fills without the burst a refresh makes.
        AddHostedProcessor<
            SuggestionCatalogProcessor,
            ISuggestionCatalogProcessor,
            SuggestionCatalogBackgroundService>(services);

        // Suggested: one suggested book per interval checked for a release an indexer
        // holds, on its own pace so abook.link and Audible never wait on each other.
        AddHostedProcessor<
            SuggestionReleaseCheckProcessor,
            ISuggestionReleaseCheckProcessor,
            SuggestionReleaseCheckBackgroundService>(services);

        // Discovery: one chart page, board page or metadata lookup per interval, so a
        // followed chart fills out over an hour instead of in one burst at startup.
        AddHostedProcessor<
            DiscoverFetchProcessor,
            IDiscoverFetchProcessor,
            DiscoverBackgroundService>(services);
        return services;
    }

    private static void AddProcessor<TProcessor, TContract>(IServiceCollection services)
        where TProcessor : class, TContract
        where TContract : class
    {
        services.AddSingleton<TProcessor>();
        services.AddSingleton<TContract>(provider =>
            provider.GetRequiredService<TProcessor>());
    }

    private static void AddHostedProcessor<TProcessor, TContract, THostedService>(
        IServiceCollection services)
        where TProcessor : class, TContract
        where TContract : class
        where THostedService : class, IHostedService
    {
        AddProcessor<TProcessor, TContract>(services);
        services.AddSingleton<THostedService>();
        services.AddHostedService(provider =>
            provider.GetRequiredService<THostedService>());
    }
}
