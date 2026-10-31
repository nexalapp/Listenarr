namespace Listenarr.Application.Audiobooks.Contracts;

public interface IAudiobookUpdatePublisher
{
    Task PublishCurrentAsync(
        int audiobookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells connected pages a book is gone, so a count or list held in the browser drops
    /// it without waiting for a reload.
    /// </summary>
    Task PublishDeletedAsync(
        int audiobookId,
        CancellationToken cancellationToken = default);
}
