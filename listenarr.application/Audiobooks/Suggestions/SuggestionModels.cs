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
namespace Listenarr.Application.Audiobooks.Suggestions
{
    /// <summary>
    /// Everything the Suggested page shows, computed from what the database already holds.
    ///
    /// Nothing here reaches the network. The catalogs it reads were cached when someone
    /// opened an author or series page, when a book was added, or when a refresh was
    /// asked for; <see cref="Coverage"/> says how much of the library those cover so the
    /// page can be honest about what it has not looked at.
    /// </summary>
    public sealed record SuggestionSnapshot(
        IReadOnlyList<AuthorSuggestionGroup> Authors,
        IReadOnlyList<SeriesSuggestionGroup> Series,
        IReadOnlyList<RelatedAuthorSuggestion> RelatedAuthors,
        SuggestionCoverage Coverage,
        IReadOnlyList<IgnoredSuggestion> Ignored);

    /// <summary>A book the user said no to, listed so the decision can be undone.</summary>
    public sealed record IgnoredSuggestion(string Key, string Title, string? Author, DateTime DismissedAt);

    /// <summary>What the page sends to ignore a book: enough to build its key and label it.</summary>
    public sealed record IgnoreSuggestionRequest(string? Asin, string Title, IReadOnlyList<string>? Authors);

    /// <summary>Books an author in the library has that the library does not.</summary>
    public sealed record AuthorSuggestionGroup(
        string Author,
        string? AuthorAsin,
        string? ImageUrl,
        int LibraryCount,
        bool Monitored,
        IReadOnlyList<SuggestedBook> Missing);

    /// <summary>Books in a series the library has started that it is missing.</summary>
    public sealed record SeriesSuggestionGroup(
        string Series,
        string? SeriesAsin,
        int LibraryCount,
        bool Monitored,
        IReadOnlyList<SuggestedBook> Missing);

    /// <summary>An author Audible lists as similar to one or more the library holds.</summary>
    public sealed record RelatedAuthorSuggestion(
        string Name,
        string? Asin,
        IReadOnlyList<string> Because);

    /// <summary>
    /// A book to offer. Carries the same fields as a catalog entry so the page can hand
    /// it straight to the add flow the author and series pages already use.
    /// </summary>
    public sealed record SuggestedBook(
        string? Asin,
        string Title,
        string? Subtitle,
        IReadOnlyList<string> Authors,
        IReadOnlyList<string> Narrators,
        string? ImageUrl,
        int? Runtime,
        string? Language,
        string? Publisher,
        IReadOnlyList<string> Genres,
        string? Series,
        string? SeriesNumber,
        string? PublishedDate,
        string? Isbn,
        string? Link,
        string? MetadataSource,
        double? RatingOverall,
        int? RatingCount,
        double? RatingStory,
        string? Description);

    /// <summary>How much of the library the cached catalogs cover.</summary>
    public sealed record SuggestionCoverage(
        int AuthorsInLibrary,
        int AuthorsWithCatalog,
        int SeriesInLibrary,
        int SeriesWithCatalog);

    /// <summary>What a refresh is doing, for the page to show.</summary>
    public sealed record SuggestionRefreshStatus(
        bool Running,
        int Completed,
        int Total,
        string? Current,
        DateTime? StartedAt,
        DateTime? FinishedAt,
        int Failed);
}
