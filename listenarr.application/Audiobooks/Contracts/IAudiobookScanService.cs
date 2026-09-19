using Listenarr.Domain.Common;

namespace Listenarr.Application.Audiobooks.Contracts;

public sealed record AudiobookScanCommand(
    int AudiobookId,
    string ScanRoot,
    PathIdentitySnapshot ScanIdentity,
    ScanPathPhysicalIdentity ScanPhysicalIdentity,
    bool MoveOwned = false,
    bool AllowReconciliation = true,
    bool IsAuthoritativeScope = true,
    string Source = "Scan",
    string CorrelationId = "");

public sealed record AudiobookScanDiagnostic(
    string Code,
    string? Path,
    string Message);

/// <summary>A tracked file the scan did not find at its path. Its row is kept, flagged.</summary>
public sealed record AudiobookScanNotFoundFile(
    int Id,
    string? Path,
    DateTime? NotFoundSinceUtc);

public sealed record AudiobookScanResult(
    Audiobook Audiobook,
    IReadOnlyList<string> AttributedFiles,
    int CreatedCount,
    IReadOnlyList<AudiobookScanNotFoundFile> NotFoundFiles,
    string? BasePath,
    bool IsComplete,
    bool ReconciliationPerformed,
    IReadOnlyList<AudiobookScanDiagnostic> Diagnostics);

public interface IAudiobookScanService
{
    Task<AudiobookScanResult> ScanAsync(
        AudiobookScanCommand command,
        CancellationToken cancellationToken = default);

    Task<bool> RegisterExistingFileAsync(
        int audiobookId,
        string audiobookBasePath,
        string filePath,
        string source = "manual-import",
        CancellationToken cancellationToken = default);
}
