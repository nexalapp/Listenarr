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
namespace Listenarr.Application.Audiobooks.Catalog;

/// <summary>
/// Whether a book being added is one the library already holds.
/// </summary>
/// <remarks>
/// There are two add paths - the application service and the API workflow - and one
/// rule between them, deliberately. Two implementations of "is this the same book"
/// would eventually disagree, and a library that accepts a book down one path and
/// refuses it down the other is worse than either answer.
/// </remarks>
public static class AudiobookEditionIdentity
{
    /// <summary>
    /// The audiobook already held that is the same edition as <paramref name="metadata"/>,
    /// or null when nothing held matches.
    /// </summary>
    /// <remarks>
    /// Every book carrying the identifier is considered rather than whichever one the
    /// database returns first, because an identifier is not unique in this library.
    /// </remarks>
    public static async Task<Audiobook?> FindExistingEditionAsync(
        IAudiobookRepository repository,
        AudibleBookMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(metadata);

        if (!string.IsNullOrWhiteSpace(metadata.Asin))
        {
            var sharingAsin = await repository.GetAllByAsinAsync(metadata.Asin);
            cancellationToken.ThrowIfCancellationRequested();
            var match = sharingAsin.FirstOrDefault(existing => RepresentsSameEdition(existing, metadata));
            if (match != null)
            {
                return match;
            }
        }

        var isbn = (metadata.Isbn ?? Enumerable.Empty<string>())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            var sharingIsbn = await repository.GetAllByIsbnAsync(isbn);
            cancellationToken.ThrowIfCancellationRequested();
            var match = sharingIsbn.FirstOrDefault(existing => RepresentsSameEdition(existing, metadata));
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an audiobook already held is the same edition as the one being added.
    /// </summary>
    /// <remarks>
    /// A shared identifier is not enough on its own. Audible returns one collection
    /// ASIN for every novella in it - "Dilation Sleep", "Nightingale" and
    /// "Grafenwalder's Bestiary" all answer to B002V8MRS2 - so matching on the ASIN
    /// alone lets whichever novella imported first lock the rest of its collection out
    /// of the library. Two narrations of one book hit the same wall when both are
    /// matched to the same product.
    ///
    /// The title and the narrators are what separate those from a genuine re-import,
    /// where all three agree. A book with no narrator recorded on either side compares
    /// equal on that count, leaving the title to carry the distinction rather than
    /// manufacturing a difference out of missing data.
    /// </remarks>
    public static bool RepresentsSameEdition(Audiobook existing, AudibleBookMetadata incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        if (!EquivalentText(existing.Title, incoming.Title))
        {
            return false;
        }

        return EquivalentNarrators(existing.Narrators, IncomingNarrators(incoming));
    }

    /// <summary>
    /// The narrators as the incoming metadata carries them, from either the list or the
    /// single legacy field, whichever the source populated.
    /// </summary>
    private static IEnumerable<string> IncomingNarrators(AudibleBookMetadata incoming)
    {
        if (incoming.Narrators is { Count: > 0 })
        {
            return incoming.Narrators;
        }

        return string.IsNullOrWhiteSpace(incoming.Narrator)
            ? []
            : incoming.Narrator.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool EquivalentText(string? left, string? right) =>
        string.Equals(
            left?.Trim() ?? string.Empty,
            right?.Trim() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

    private static bool EquivalentNarrators(
        IEnumerable<string>? left,
        IEnumerable<string>? right) =>
        Normalize(left).SequenceEqual(Normalize(right), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> Normalize(IEnumerable<string>? values) =>
        (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
}
