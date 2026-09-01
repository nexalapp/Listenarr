using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lookups by external identifier.
/// </summary>
/// <remarks>
/// An identifier does not single out one book here. Audible publishes a whole
/// collection of novellas under one ASIN, so the "all" variants exist for callers that
/// have to weigh every namesake rather than trust whichever row came back first - see
/// AudiobookEditionIdentity.
/// </remarks>
public partial class AudiobookRepository
{
    public async Task<Audiobook?> GetByAsinAsync(string asin)
    {
        var normalizedAsin = NormalizeAsin(asin);
        if (string.IsNullOrWhiteSpace(normalizedAsin)) return null;

        return await _db.Audiobooks
            .Include(a => a.ExternalIdentifiers)
            .FirstOrDefaultAsync(a =>
                (a.Asin != null && a.Asin.ToUpper() == normalizedAsin) ||
                (a.ExternalIdentifiers != null && a.ExternalIdentifiers.Any(i =>
                    i.Type == AudiobookExternalIdentifierType.Asin &&
                    i.ValueNormalized == normalizedAsin)));
    }

    public async Task<Audiobook?> GetByIsbnAsync(string isbn)
    {
        var normalizedIsbn = NormalizeIsbn(isbn);
        if (string.IsNullOrWhiteSpace(normalizedIsbn)) return null;

        var audiobooks = await _db.Audiobooks
            .Include(a => a.ExternalIdentifiers)
            .ToListAsync();

        return audiobooks.FirstOrDefault(a =>
            (a.Isbn != null && a.Isbn.Any(i => NormalizeIsbn(i) == normalizedIsbn)) ||
            (a.ExternalIdentifiers != null && a.ExternalIdentifiers.Any(i =>
                i.Type == AudiobookExternalIdentifierType.Isbn &&
                string.Equals(i.ValueNormalized, normalizedIsbn, StringComparison.OrdinalIgnoreCase))));
    }

    public async Task<IReadOnlyList<Audiobook>> GetAllByAsinAsync(string asin)
    {
        var normalizedAsin = NormalizeAsin(asin);
        if (string.IsNullOrWhiteSpace(normalizedAsin)) return [];

        return await _db.Audiobooks
            .Include(a => a.ExternalIdentifiers)
            .Where(a =>
                (a.Asin != null && a.Asin.ToUpper() == normalizedAsin) ||
                (a.ExternalIdentifiers != null && a.ExternalIdentifiers.Any(i =>
                    i.Type == AudiobookExternalIdentifierType.Asin &&
                    i.ValueNormalized == normalizedAsin)))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Audiobook>> GetAllByIsbnAsync(string isbn)
    {
        var normalizedIsbn = NormalizeIsbn(isbn);
        if (string.IsNullOrWhiteSpace(normalizedIsbn)) return [];

        var audiobooks = await _db.Audiobooks
            .Include(a => a.ExternalIdentifiers)
            .ToListAsync();

        return audiobooks.Where(a =>
            (a.Isbn != null && a.Isbn.Any(i => NormalizeIsbn(i) == normalizedIsbn)) ||
            (a.ExternalIdentifiers != null && a.ExternalIdentifiers.Any(i =>
                i.Type == AudiobookExternalIdentifierType.Isbn &&
                string.Equals(i.ValueNormalized, normalizedIsbn, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }
}
