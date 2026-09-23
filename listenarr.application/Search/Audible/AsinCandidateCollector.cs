using System.Text.RegularExpressions;
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
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Search.Audible;

/// <summary>
/// Collects ASIN candidates from OpenLibrary and other non-scraping sources.
/// </summary>
public class AsinCandidateCollector
{
    private readonly ILogger<AsinCandidateCollector> _logger;
    private readonly IOpenLibraryService _openLibraryService;
    private readonly IOverDriveService? _overDriveService;
    private readonly MetadataConverters _metadataConverters;
    private readonly SearchProgressReporter _searchProgressReporter;

    public AsinCandidateCollector(
        ILogger<AsinCandidateCollector> logger,
        IOpenLibraryService openLibraryService,
        MetadataConverters metadataConverters,
        SearchProgressReporter searchProgressReporter,
        IOverDriveService? overDriveService = null)
    {
        _logger = logger;
        _openLibraryService = openLibraryService;
        _overDriveService = overDriveService;
        _metadataConverters = metadataConverters;
        _searchProgressReporter = searchProgressReporter;
    }

    /// <summary>
    /// Collects ASIN candidates from non-scraping sources.
    /// </summary>
    public async Task<AsinCandidateCollection> CollectCandidatesAsync(
        string query,
        bool skipOpenLibrary = false,
        CancellationToken ct = default,
        string? title = null,
        string? author = null)
    {
        var collection = new AsinCandidateCollection();

        // The composed query still carries its "AUTHOR:"/"TITLE:" prefixes. OpenLibrary's
        // normalizer drops the colons, which turns the prefixes into literal search tokens and
        // matches nothing, so prefer the fields the caller already parsed.
        var searchTitle = !string.IsNullOrWhiteSpace(title) ? title : StripSearchPrefixes(query);
        var searchAuthor = !string.IsNullOrWhiteSpace(author) ? author : null;

        _logger.LogInformation(
            "Collecting candidates from OpenLibrary (title='{Title}', author='{Author}')",
            searchTitle,
            searchAuthor);

        // Augment ASIN candidates with OpenLibrary suggestions
        if (!skipOpenLibrary && !string.IsNullOrWhiteSpace(searchTitle))
        {
            ct.ThrowIfCancellationRequested();
            await CollectOpenLibraryCandidatesAsync(searchTitle, searchAuthor, collection, ct);
        }

        // A library catalogue last, because it answers a narrower question than the shops
        // and only sometimes: it lends what publishers license to libraries, which is a
        // partly different set. It is asked because it names the reader, and a book whose
        // audio credits someone no shop admits exists is exactly what it can settle.
        if (_overDriveService != null && !string.IsNullOrWhiteSpace(searchTitle))
        {
            ct.ThrowIfCancellationRequested();
            await CollectOverDriveCandidatesAsync(searchTitle, searchAuthor, collection, ct);
        }

        return collection;
    }

    /// <summary>
    /// Removes "FIELD:" prefixes from a composed query so it can be used as a plain search term.
    /// Only used when the caller did not supply already-parsed fields.
    /// </summary>
    private static string StripSearchPrefixes(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var stripped = Regex.Replace(
            query,
            @"\b(?:ASIN|ISBN|AUTHOR|TITLE|SERIES|NARRATOR)\s*:\s*",
            " ",
            RegexOptions.IgnoreCase);

        return Regex.Replace(stripped, @"\s{2,}", " ").Trim();
    }

    /// <summary>
    /// Editions a public library lends, which carry the one field the other sources so
    /// often lack: who read it.
    /// </summary>
    private async Task CollectOverDriveCandidatesAsync(
        string title,
        string? author,
        AsinCandidateCollection collection,
        CancellationToken ct = default)
    {
        IReadOnlyList<OverDriveEdition> editions;
        try
        {
            editions = await _overDriveService!.SearchAsync(title, author, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "OverDrive could not be asked about {Title}", title);
            return;
        }

        foreach (var edition in editions)
        {
            ct.ThrowIfCancellationRequested();
            var metadata = new AudibleBookMetadata
            {
                Asin = null,
                Source = "OverDrive",
                Title = TitleCleanup.StripNarratorSuffix(TitleCasing.ToTitleCase(edition.Title)),
                Authors = edition.Authors.ToList(),
                Narrators = edition.Narrators.ToList(),
                Publisher = edition.Publisher,
                PublishYear = edition.PublishYear,
                Runtime = edition.RuntimeMinutes,
                ImageUrl = edition.ImageUrl
            };

            try
            {
                var searchResult = await _metadataConverters.ConvertMetadataToSearchResultAsync(metadata, string.Empty);
                searchResult.IsEnriched = true;
                searchResult.MetadataSource = "OverDrive";
                searchResult.Id = $"overdrive:{edition.Id}";
                collection.AlternateEditionResults.Add(searchResult);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not render an OverDrive edition of {Title}", edition.Title);
            }
        }

        if (editions.Count > 0)
        {
            _logger.LogInformation("OverDrive offered {Count} edition(s) of {Title}", editions.Count, title);
        }
    }

    private async Task CollectOpenLibraryCandidatesAsync(string query, string? author, AsinCandidateCollection collection, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await _searchProgressReporter.BroadcastAsync($"Searching OpenLibrary for additional titles", null);
            var books = await _openLibraryService.SearchBooksAsync(query, author, 5);

            foreach (var book in books.Docs.Take(3))
            {
                ct.ThrowIfCancellationRequested();
                // An exact title match is the strongest candidate, not one to discard: skipping
                // titles equal to the query silently threw away the best result for every book
                // whose catalogue title matches what was searched for.
                if (!string.IsNullOrEmpty(book.Title))
                {
                    _logger.LogInformation("OpenLibrary suggested title: {Title}", book.Title);
                    await _searchProgressReporter.BroadcastAsync($"OpenLibrary found: {book.Title}", null);

                    // Convert OpenLibrary work/edition into minimal AudibleBookMetadata and SearchResult
                    try
                    {
                        string? coverUrl = null;
                        if (book.CoverId.HasValue && book.CoverId.Value > 0)
                        {
                            coverUrl = $"https://covers.openlibrary.org/b/id/{book.CoverId}-L.jpg";
                        }

                        var metadata = new AudibleBookMetadata
                        {
                            Asin = null,
                            Source = "OpenLibrary",
                            // Open Library sentence-cases its titles; raise them so the book
                            // is not shelved as "Fortress of owls".
                            Title = TitleCasing.ToTitleCase(book.Title),
                            Authors = book.AuthorName?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList(),
                            Publisher = (book.Publisher?.Count > 1) ? "Multiple" : book.Publisher?.FirstOrDefault(),
                            PublishYear = book.FirstPublishYear?.ToString(),
                            Description = null,
                            ImageUrl = coverUrl,
                            OpenLibraryId = book.Key
                        };

                        ct.ThrowIfCancellationRequested();
                        var searchResult = await _metadataConverters.ConvertMetadataToSearchResultAsync(metadata, string.Empty);
                        searchResult.IsEnriched = true;
                        searchResult.MetadataSource = "OpenLibrary";

                        // If OpenLibrary provides a canonical key (work or edition), expose it
                        if (!string.IsNullOrWhiteSpace(book.Key))
                        {
                            // Use OpenLibrary Key as the Id instead of random GUID
                            searchResult.Id = book.Key;

                            if (book.Key.StartsWith("/works", StringComparison.OrdinalIgnoreCase))
                            {
                                searchResult.ProductUrl = $"https://openlibrary.org{book.Key}";
                                searchResult.ResultUrl = $"https://openlibrary.org{book.Key}.json";
                            }
                            else if (book.Key.StartsWith("/books", StringComparison.OrdinalIgnoreCase))
                            {
                                searchResult.ProductUrl = $"https://openlibrary.org{book.Key}";
                                searchResult.ResultUrl = $"https://openlibrary.org{book.Key}.json";
                            }
                        }

                        collection.CatalogueDerivedResults.Add(searchResult);

                        // Only store in dictionary if we have a valid OpenLibrary Key
                        // Don't use GUID fallback as it creates invalid openLibraryId values
                        if (!string.IsNullOrWhiteSpace(book.Key))
                        {
                            collection.AsinToOpenLibrary[book.Key] = book;
                        }
                    }
                    catch (Exception exConvert) when (exConvert is not OperationCanceledException && exConvert is not OutOfMemoryException && exConvert is not StackOverflowException)
                    {
                        _logger.LogWarning(exConvert, "Failed to convert OpenLibrary book to SearchResult: {Title}", book.Title);
                    }
                }
            }
        }
        catch (Exception exOL) when (exOL is not OperationCanceledException && exOL is not OutOfMemoryException && exOL is not StackOverflowException)
        {
            _logger.LogWarning(exOL, "OpenLibrary augmentation failed: {Message}", exOL.Message);
        }
    }
}

