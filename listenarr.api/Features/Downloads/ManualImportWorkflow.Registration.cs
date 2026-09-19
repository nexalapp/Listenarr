namespace Listenarr.Api.Features.Downloads;

public sealed partial class ManualImportWorkflow
{
    private Task<bool> RegisterPublishedManualImportAsync(
        Audiobook audiobook,
        AudiobookFileOwnershipCheckResult initialOwnership,
        IAudiobookFileRegistrationLease registrationLease,
        string authoritativeBasePath,
        CancellationToken cancellationToken)
    {
        return _audiobookFileService.RegisterPublishedGenerationWithBasePathAsync(
            audiobook,
            initialOwnership,
            registrationLease,
            authoritativeBasePath,
            "manual-import",
            cancellationToken);
    }
}
