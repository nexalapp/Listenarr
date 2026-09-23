using Listenarr.Domain.Audiobooks.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public partial class AudiobookRepository
{
    public async Task<bool> TryUpdateBasePathAsync(
        int audiobookId,
        string expectedBasePath,
        string newBasePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedBasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newBasePath);

        if (_db.Database.IsRelational())
        {
            var affected = await _db.Audiobooks
                .Where(audiobook => audiobook.Id == audiobookId
                    && audiobook.BasePath == expectedBasePath)
                .ExecuteUpdateAsync(
                    updates => updates.SetProperty(
                        audiobook => audiobook.BasePath,
                        newBasePath),
                    ct);
            if (affected == 1)
            {
                SynchronizeTrackedBasePath(audiobookId, newBasePath);
                return true;
            }

            return false;
        }

        var currentBasePath = await _db.Audiobooks
            .AsNoTracking()
            .Where(audiobook => audiobook.Id == audiobookId)
            .Select(audiobook => audiobook.BasePath)
            .SingleOrDefaultAsync(ct);
        if (!string.Equals(currentBasePath, expectedBasePath, StringComparison.Ordinal))
        {
            return false;
        }

        var entry = _db.ChangeTracker.Entries<Audiobook>()
            .FirstOrDefault(candidate => candidate.Entity.Id == audiobookId);
        if (entry == null)
        {
            entry = _db.Attach(new Audiobook
            {
                Id = audiobookId,
                BasePath = expectedBasePath
            });
        }

        entry.Property(audiobook => audiobook.BasePath).CurrentValue = newBasePath;
        entry.Property(audiobook => audiobook.BasePath).IsModified = true;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> TryUpdateImageUrlAsync(
        int audiobookId,
        string? expectedImageUrl,
        string? newImageUrl,
        CancellationToken ct = default)
    {
        if (_db.Database.IsRelational())
        {
            var affected = await _db.Audiobooks
                .Where(audiobook => audiobook.Id == audiobookId
                    && audiobook.ImageUrl == expectedImageUrl)
                .ExecuteUpdateAsync(
                    updates => updates.SetProperty(
                        audiobook => audiobook.ImageUrl,
                        newImageUrl),
                    ct);
            if (affected != 1)
            {
                return false;
            }

            SynchronizeTrackedImageUrl(audiobookId, newImageUrl);
            return true;
        }

        var existing = await _db.Audiobooks
            .FirstOrDefaultAsync(
                audiobook => audiobook.Id == audiobookId,
                ct);
        if (existing == null
            || !string.Equals(
                existing.ImageUrl,
                expectedImageUrl,
                StringComparison.Ordinal))
        {
            return false;
        }

        existing.ImageUrl = newImageUrl;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetAudioAuditAcceptedAsync(int audiobookId, bool accepted, DateTime nowUtc, CancellationToken ct = default)
    {
        var existing = await _db.Audiobooks.FirstOrDefaultAsync(candidate => candidate.Id == audiobookId, ct);
        if (existing == null)
        {
            return false;
        }

        if (!accepted)
        {
            existing.AudioAuditAcceptedIdentity = null;
            existing.AudioAuditAcceptedVerdict = null;
            existing.AudioAuditAcceptedAt = null;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // Nothing to vouch for, and nothing to pin it to: a book nobody has listened to
        // cannot have its verdict overruled.
        if (string.IsNullOrWhiteSpace(existing.AudioAuditFileIdentity))
        {
            return false;
        }

        existing.AudioAuditAcceptedIdentity = existing.AudioAuditFileIdentity;
        existing.AudioAuditAcceptedVerdict = existing.AudioAuditVerdict;
        existing.AudioAuditAcceptedAt = nowUtc;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SetAudioAuditAsync(int audiobookId, AudioAuditRecord audit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        var existing = await _db.Audiobooks.FirstOrDefaultAsync(candidate => candidate.Id == audiobookId, ct);
        if (existing == null)
        {
            return;
        }

        existing.AudioAuditVerdict = audit.Verdict;
        existing.AudioAuditReason = Truncate(audit.Reason, 512);
        existing.AudioAuditHeard = Truncate(audit.Heard, 4000);
        existing.AudioAuditHeardTitle = Truncate(audit.Credits.Title, 256);
        existing.AudioAuditHeardAuthor = Truncate(audit.Credits.Author, 256);
        existing.AudioAuditHeardNarrator = Truncate(audit.Credits.Narrator, 256);
        existing.AudioAuditFileIdentity = Truncate(audit.FileIdentity, 256);
        // Acceptance is deliberately left alone. It is pinned to the files it was given
        // for, so listening again to the same recording keeps it and a recording swapped
        // in fails the identity check on its own.
        existing.AudioAuditedAt = audit.AuditedAtUtc;
        await _db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max) =>
        value == null || value.Length <= max ? value : value[..max];

    public async Task<bool> UpdateAsync(Audiobook audiobook)
    {
        ArgumentNullException.ThrowIfNull(audiobook);

        var entry = _db.Entry(audiobook);
        var tracked = entry;
        if (entry.State == EntityState.Detached)
        {
            var existing = await _db.Audiobooks.FirstOrDefaultAsync(candidate => candidate.Id == audiobook.Id);
            if (existing == null)
            {
                return false;
            }

            tracked = _db.Entry(existing);

            var preservedBasePath = existing.BasePath;
            var preservedFilePath = existing.FilePath;
            var preservedFileSize = existing.FileSize;
            var preservedImageUrl = existing.ImageUrl;
            _db.Entry(existing).CurrentValues.SetValues(audiobook);
            // A detached entity has no original-value snapshot, so any path-bearing value may
            // be stale. Path changes must use a tracked entity or the dedicated expected-source
            // rewrite contract.
            existing.BasePath = preservedBasePath;
            existing.FilePath = preservedFilePath;
            existing.FileSize = preservedFileSize;
            existing.ImageUrl = preservedImageUrl;
        }

        // Tracked entities retain EF's original-value snapshot, so SaveChanges writes only
        // properties the caller actually changed. Calling Update here would mark BasePath and
        // every other property modified, allowing an unrelated stale metadata save to undo a
        // completed move from another DbContext.
        await RejudgeAudioAuditAsync(tracked);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// The audio audit's verdict is what the transcript says against the record, so a
    /// record that changes — a fix-match, a corrected narrator — is judged again from the
    /// transcript already stored. Nothing is listened to; the book stops showing a
    /// mismatch it no longer has the moment the record is right.
    /// </summary>
    private async Task RejudgeAudioAuditAsync(EntityEntry<Audiobook> entry)
    {
        var audiobook = entry.Entity;
        if (audiobook.AudioAuditVerdict == AudioAuditVerdict.NotAudited || string.IsNullOrWhiteSpace(audiobook.AudioAuditHeard))
        {
            return;
        }

        var original = entry.OriginalValues;
        var sameTitle = string.Equals(original.GetValue<string?>(nameof(Audiobook.Title)), audiobook.Title, StringComparison.Ordinal);
        var sameAuthors = SameNames(original.GetValue<List<string>?>(nameof(Audiobook.Authors)), audiobook.Authors);
        var sameNarrators = SameNames(original.GetValue<List<string>?>(nameof(Audiobook.Narrators)), audiobook.Narrators);
        if (sameTitle && sameAuthors && sameNarrators)
        {
            return;
        }

        var aliasesJson = await _db.ApplicationSettings
            .AsNoTracking()
            .Select(settings => settings.AuthorAliasesJson)
            .FirstOrDefaultAsync();
        var result = AudioIdentityMatcher.Judge(
            audiobook.AudioAuditHeard,
            audiobook.Title,
            audiobook.Authors,
            audiobook.Narrators,
            AuthorAliases.Parse(aliasesJson));
        audiobook.AudioAuditVerdict = result.Verdict;
        audiobook.AudioAuditReason = Truncate(result.Reason, 512);
    }

    private static bool SameNames(List<string>? left, List<string>? right) =>
        (left ?? []).SequenceEqual(right ?? [], StringComparer.Ordinal);

    private void SynchronizeTrackedImageUrl(
        int audiobookId,
        string? newImageUrl)
    {
        var trackedEntry = _db.ChangeTracker.Entries<Audiobook>()
            .FirstOrDefault(entry => entry.Entity.Id == audiobookId);
        if (trackedEntry == null)
        {
            return;
        }

        var property = trackedEntry.Property(audiobook => audiobook.ImageUrl);
        property.CurrentValue = newImageUrl;
        property.OriginalValue = newImageUrl;
        property.IsModified = false;
    }

    private void SynchronizeTrackedBasePath(int audiobookId, string newBasePath)
    {
        var trackedEntry = _db.ChangeTracker.Entries<Audiobook>()
            .FirstOrDefault(entry => entry.Entity.Id == audiobookId);
        if (trackedEntry == null)
        {
            return;
        }

        var property = trackedEntry.Property(audiobook => audiobook.BasePath);
        property.CurrentValue = newBasePath;
        property.OriginalValue = newBasePath;
        property.IsModified = false;
    }

    public async Task<bool> UpdateWithIdentifierReplaceAsync(
        Audiobook audiobook,
        List<AudiobookExternalIdentifier> newIdentifiers,
        CancellationToken ct = default)
    {
        var existing = await _db.AudiobookExternalIdentifiers
            .Where(identifier => identifier.AudiobookId == audiobook.Id)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            _db.AudiobookExternalIdentifiers.RemoveRange(existing);
        }

        foreach (var identifier in newIdentifiers)
        {
            identifier.AudiobookId = audiobook.Id;
        }

        if (newIdentifiers.Count > 0)
        {
            _db.AudiobookExternalIdentifiers.AddRange(newIdentifiers);
        }

        audiobook.ExternalIdentifiers = newIdentifiers;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
