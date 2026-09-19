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
using Listenarr.Application.Common.Exceptions;
using Listenarr.Domain.Common;
using Listenarr.Api.Dtos.ManualImport;

namespace Listenarr.Api.Features.Downloads;

/// <summary>
/// Import files an operator has matched to library records, from any directory.
/// </summary>
/// <remarks>
/// The whole manual-import batch — ordering, recovery of an interrupted move, the
/// per-item publication under the audiobook locks, companion files, source-folder
/// cleanup and the focused scans afterwards — lives here rather than in the
/// controller so that a workflow other than an HTTP request can run one. The
/// controller maps this class's exceptions to status codes and nothing more.
/// </remarks>
public sealed partial class ManualImportWorkflow
{
    private readonly ILogger<ManualImportWorkflow> _logger;
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IMetadataService _metadataService;
    private readonly IFileNamingService _fileNamingService;
    private readonly IConfigurationService _configService;
    private readonly IScanQueueService _scanQueueService;
    private readonly IAudiobookScanService _audiobookScanService;
    private readonly IScanPathAuthorizationService _scanPathAuthorizationService;
    private readonly IRootFolderService _rootFolderService;
    private readonly IFileMover _fileMover;
    private readonly IFilePublicationSourceCapability _filePublicationSourceCapability;
    private readonly IFilePublicationCapabilityResolver?
        _filePublicationCapabilityResolver;
    private readonly IAudiobookFileService _audiobookFileService;
    private readonly IFileSystem _fileSystem;
    private readonly IFileSystemSemanticsResolver _semanticsResolver;
    private readonly IFilesystemMutationCoordinator _filesystemMutationCoordinator;
    private readonly IAudiobookOperationCoordinator _audiobookOperationCoordinator;
    private readonly IFileRegistrationRecoveryService _fileRegistrationRecoveryService;
    private readonly IMoveQueueService _moveQueueService;
    private readonly ILibraryFilesystemMutationGate _filesystemMutationGate;
    private readonly ManualImportPathPlanner _pathPlanner;
    private readonly ManualImportCompanionImporter _companionImporter;
    private readonly ILibraryDirectoryOwnershipStore _directoryOwnershipStore;

    public ManualImportWorkflow(
        ILogger<ManualImportWorkflow> logger,
        IAudiobookRepository audiobookRepository,
        IMetadataService metadataService,
        IFileNamingService fileNamingService,
        IConfigurationService configService,
        IScanQueueService scanQueueService,
        IAudiobookScanService audiobookScanService,
        IScanPathAuthorizationService scanPathAuthorizationService,
        IRootFolderService rootFolderService,
        IFileMover fileMover,
        IFilePublicationSourceCapability filePublicationSourceCapability,
        IAudiobookFileService audiobookFileService,
        IFileSystem fileSystem,
        IFileSystemSemanticsResolver semanticsResolver,
        IFilesystemMutationCoordinator filesystemMutationCoordinator,
        IAudiobookOperationCoordinator audiobookOperationCoordinator,
        IFileRegistrationRecoveryService fileRegistrationRecoveryService,
        IMoveQueueService moveQueueService,
        ILibraryDirectoryOwnershipStore directoryOwnershipStore,
        ILibraryFilesystemMutationGate filesystemMutationGate,
        ManualImportPathPlanner? pathPlanner = null,
        ManualImportCompanionImporter? companionImporter = null,
        IFilePublicationCapabilityResolver? filePublicationCapabilityResolver = null)
    {
        _logger = logger;
        _audiobookRepository = audiobookRepository;
        _metadataService = metadataService;
        _fileNamingService = fileNamingService;
        _configService = configService;
        _scanQueueService = scanQueueService;
        _audiobookScanService = audiobookScanService
            ?? throw new ArgumentNullException(nameof(audiobookScanService));
        _scanPathAuthorizationService = scanPathAuthorizationService
            ?? throw new ArgumentNullException(nameof(scanPathAuthorizationService));
        _rootFolderService = rootFolderService;
        _fileMover = fileMover;
        _filePublicationSourceCapability = filePublicationSourceCapability
            ?? throw new ArgumentNullException(nameof(filePublicationSourceCapability));
        _filePublicationCapabilityResolver = filePublicationCapabilityResolver;
        _audiobookFileService = audiobookFileService;
        _fileSystem = fileSystem;
        _semanticsResolver = semanticsResolver;
        _filesystemMutationCoordinator = filesystemMutationCoordinator ?? throw new ArgumentNullException(nameof(filesystemMutationCoordinator));
        _audiobookOperationCoordinator = audiobookOperationCoordinator ?? throw new ArgumentNullException(nameof(audiobookOperationCoordinator));
        _fileRegistrationRecoveryService = fileRegistrationRecoveryService
            ?? throw new ArgumentNullException(nameof(fileRegistrationRecoveryService));
        _moveQueueService = moveQueueService ?? throw new ArgumentNullException(nameof(moveQueueService));
        _directoryOwnershipStore = directoryOwnershipStore ?? throw new ArgumentNullException(nameof(directoryOwnershipStore));
        _filesystemMutationGate = filesystemMutationGate
            ?? throw new ArgumentNullException(nameof(filesystemMutationGate));
        _pathPlanner = pathPlanner ?? new ManualImportPathPlanner(fileNamingService);
        _companionImporter = companionImporter ?? new ManualImportCompanionImporter(
            metadataService,
            fileMover,
            filePublicationSourceCapability,
            fileSystem,
            directoryOwnershipStore,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ManualImportCompanionImporter>.Instance,
            audiobookFileService,
            filePublicationCapabilityResolver);
    }

    /// <summary>
    /// Import every item in the request. Throws <see cref="ArgumentException"/> for a
    /// request with no path or no items, <see cref="DirectoryNotFoundException"/> for a
    /// source that is not there, and <see cref="ApplicationConflictException"/> for a
    /// conflict the caller should surface as such.
    /// </summary>
    public async Task<ManualImportBatchResult> StartAsync(
        ManualImportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            throw new ArgumentException("Path is required.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sourceDirectory = Path.GetFullPath(request.Path);
        if (!_fileSystem.DirectoryExists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {sourceDirectory}");
        }

        if (request.Items == null || !request.Items.Any())
        {
            throw new ArgumentException("No items to import.", nameof(request));
        }

        _filesystemMutationGate.EnsureReady();

        var results = new List<ManualImportResultDto>();
        var destinationTracker = new ManualImportDestinationTracker(
        _fileSystem,
        _filePublicationSourceCapability);

        // Fetch root folders once for the whole batch (used for path containment validation)
        var rootFolders = await _rootFolderService.GetAllAsync();
        var appSettings = await _configService.GetApplicationSettingsAsync();
        var sourceSemantics = await ResolvePathSemanticsAsync(
            sourceDirectory,
            rootFolders,
            "Source filesystem identity is unavailable.",
            cancellationToken);
        var orderedItems = ManualImportPathPlanner.BuildOrderedItems(
            request.Items,
            sourceSemantics.Comparer);
        var selectedAudioProfiles = request.IncludeCompanionFiles
            && request.Action != FileAction.None
            ? await _companionImporter.BuildAudioMatchProfilesAsync(
                orderedItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
                    .Select(item => item.FullPath!)
                    .Where(FileUtils.IsAudioFile),
                sourceSemantics.Comparer,
                cancellationToken)
            : Array.Empty<FileUtils.AudioMatchProfile>();

        _logger.LogDebug("Manual import batch: {ItemCount} items", orderedItems.Count);
        var stoppedByCancellation = false;

        await ExecuteWithAudiobookLocksAsync(
            orderedItems.Select(item => item.MatchedAudiobookId),
            orderedItems
                .Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
                .Select(item => item.FullPath!)
                .ToArray(),
            async (recoveryReceipts, operationToken) =>
            {
                var planningBasePaths = new Dictionary<int, string>();
                var consumedRecoveryOperationIds = new HashSet<Guid>();
                var planningDestinationResolutions =
                    new Dictionary<int, FileSystemSemanticsResolution>();
                var seriesPositionStyles =
                    await _audiobookRepository.GetSeriesPositionStylesAsync(operationToken);
                try
                {
                    foreach (var item in orderedItems)
                    {
                        operationToken.ThrowIfCancellationRequested();
                        var fileCount = orderedItems.Count(candidate =>
                            candidate.MatchedAudiobookId == item.MatchedAudiobookId);
                        _logger.LogDebug(
                            "Importing item {Index}: {Path} for audiobook {AudiobookId}, fileCount: {FileCount}",
                            orderedItems.IndexOf(item),
                            item.FullPath,
                            item.MatchedAudiobookId,
                            fileCount);
                        var recoveredResult = await TryConsumeRecoveredManualImportAsync(
                            item,
                            request.Action,
                            sourceSemantics,
                            recoveryReceipts,
                            consumedRecoveryOperationIds,
                            destinationTracker,
                            rootFolders,
                            operationToken);
                        if (recoveredResult != null)
                        {
                            results.Add(recoveredResult);
                            _logger.LogInformation(
                                "Manual import retry reused recovered Move publication for audiobook {AudiobookId}: {Source} -> {Destination}",
                                item.MatchedAudiobookId,
                                LogRedaction.SanitizeFilePath(recoveredResult.SourcePath),
                                LogRedaction.SanitizeFilePath(recoveredResult.DestinationPath));
                            continue;
                        }

                        var result = await ImportFileAsync(
                            item,
                            request.Action,
                            sourceDirectory,
                            sourceSemantics,
                            destinationTracker,
                            planningBasePaths,
                            planningDestinationResolutions,
                            seriesPositionStyles,
                            rootFolders,
                            appSettings,
                            fileCount > 1,
                            operationToken);
                        _logger.LogDebug(
                            "Import result {Index}: Success={Success}, Destination={Destination}, Error={Error}",
                            orderedItems.IndexOf(item),
                            result.Success,
                            result.DestinationPath,
                            result.Error);
                        results.Add(result);
                    }

                    if (request.IncludeCompanionFiles && request.Action != FileAction.None)
                    {
                        var companionImportCount = await _companionImporter.ImportAsync(
                            request.Action,
                            orderedItems,
                            results,
                            sourceDirectory,
                            selectedAudioProfiles,
                            destinationTracker,
                            sourceSemantics,
                            planningDestinationResolutions,
                            appSettings.ImportBlacklistExtensions,
                            rootFolders,
                            operationToken);
                        _logger.LogInformation(
                            "Manual import companion-file pass completed with {Count} imported companion file(s)",
                            companionImportCount);
                    }

                    if (request.Action != FileAction.None
                        && request.CleanupEmptySourceFolders)
                    {
                        operationToken.ThrowIfCancellationRequested();
                        if (PotentiallyOverlapsAnyConfiguredRoot(
                                sourceDirectory,
                                rootFolders))
                        {
                            _logger.LogDebug(
                                "Skipped generic empty-directory cleanup for managed manual-import source {SourceRoot}; managed library paths require explicit filesystem mutation authority.",
                                LogRedaction.SanitizeFilePath(sourceDirectory));
                        }
                        else
                        {
                            _fileSystem.DeleteEmptyDirectories(sourceDirectory);
                        }
                    }
                }
                catch (OperationCanceledException) when (
                    results.Any(result => result.Success))
                {
                    stoppedByCancellation = true;
                }

                var hasSuccessfulCommit = results.Any(result => result.Success);
                await EnqueueFocusedScansAsync(
                    results,
                    hasSuccessfulCommit
                        ? CancellationToken.None
                        : operationToken);

                if (hasSuccessfulCommit
                    && operationToken.IsCancellationRequested)
                {
                    stoppedByCancellation = true;
                }
                else
                {
                    operationToken.ThrowIfCancellationRequested();
                }
            },
            cancellationToken);

        var successCount = results.Count(r => r.Success);
        _logger.LogInformation("Manual import batch completed: {SuccessCount}/{TotalCount} succeeded, usedDestinations: {DestinationCount}", successCount, results.Count, destinationTracker.Count);
        return new ManualImportBatchResult(successCount, orderedItems.Count, stoppedByCancellation, results);
    }
}

/// <summary>What one manual-import batch did, item by item.</summary>
public sealed record ManualImportBatchResult(
    int ImportedCount,
    int TotalCount,
    bool StoppedByCancellation,
    IReadOnlyList<ManualImportResultDto> Results);
