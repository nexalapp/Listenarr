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
using Listenarr.Application.Audiobooks.Suggestions;
using Listenarr.Domain.Audiobooks.Discover;

namespace Listenarr.Application.Audiobooks.Discover
{
    /// <summary>
    /// Between a discovery source's record of a book and the one the rest of the
    /// application passes around.
    /// </summary>
    public static class DiscoverMapping
    {
        /// <summary>
        /// A catalog search result as a discovery row. The same mapping the author and
        /// series caches use, so a book discovered in a chart and the same book found
        /// through an author page carry identical fields.
        /// </summary>
        public static DiscoverBook FromSearchResult(AudibleSearchResult book, string? fallbackAsin = null)
        {
            ArgumentNullException.ThrowIfNull(book);
            var cached = AuthorCatalogMapping.MapCachedCatalogBook(book);
            return new DiscoverBook
            {
                Asin = cached.Asin ?? fallbackAsin,
                Title = cached.Title,
                Subtitle = cached.Subtitle,
                Authors = cached.Authors,
                Narrators = cached.Narrators,
                ImageUrl = cached.ImageUrl,
                Runtime = cached.Runtime,
                Language = cached.Language,
                Publisher = cached.Publisher,
                Genres = cached.Genres,
                Series = cached.Series,
                SeriesNumber = cached.SeriesNumber,
                PublishedDate = cached.PublishedDate,
                Isbn = cached.Isbn,
                Link = cached.Link,
                MetadataSource = cached.MetadataSource,
                RatingOverall = cached.RatingOverall,
                RatingCount = cached.RatingCount,
                RatingStory = cached.RatingStory,
                Description = cached.Description
            };
        }

        public static SuggestedBook ToSuggestedBook(DiscoverBook book)
        {
            ArgumentNullException.ThrowIfNull(book);
            return new SuggestedBook(
                book.Asin,
                book.Title,
                book.Subtitle,
                book.Authors ?? [],
                book.Narrators ?? [],
                book.ImageUrl,
                book.Runtime,
                book.Language,
                book.Publisher,
                book.Genres ?? [],
                book.Series,
                book.SeriesNumber,
                book.PublishedDate,
                book.Isbn,
                book.Link,
                book.MetadataSource,
                book.RatingOverall,
                book.RatingCount,
                book.RatingStory,
                book.Description);
        }

        /// <summary>
        /// A looked-up record laid over what the source said. The source keeps the last
        /// word on series and position: a forum title states the place in the series the
        /// uploader is working through, and Audible's own catalog often states none.
        /// </summary>
        public static DiscoverBook Merge(DiscoverBook source, DiscoverBook resolved)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(resolved);

            return new DiscoverBook
            {
                Asin = resolved.Asin ?? source.Asin,
                Title = string.IsNullOrWhiteSpace(resolved.Title) ? source.Title : resolved.Title,
                Subtitle = resolved.Subtitle ?? source.Subtitle,
                Authors = resolved.Authors is { Count: > 0 } ? resolved.Authors : source.Authors,
                Narrators = resolved.Narrators is { Count: > 0 } ? resolved.Narrators : source.Narrators,
                ImageUrl = resolved.ImageUrl ?? source.ImageUrl,
                Runtime = resolved.Runtime ?? source.Runtime,
                Language = resolved.Language ?? source.Language,
                Publisher = resolved.Publisher ?? source.Publisher,
                Genres = resolved.Genres is { Count: > 0 } ? resolved.Genres : source.Genres,
                Series = source.Series ?? resolved.Series,
                SeriesNumber = source.SeriesNumber ?? resolved.SeriesNumber,
                PublishedDate = resolved.PublishedDate ?? source.PublishedDate,
                Isbn = resolved.Isbn ?? source.Isbn,
                Link = resolved.Link ?? source.Link,
                MetadataSource = resolved.MetadataSource ?? source.MetadataSource,
                RatingOverall = resolved.RatingOverall ?? source.RatingOverall,
                RatingCount = resolved.RatingCount ?? source.RatingCount,
                RatingStory = resolved.RatingStory ?? source.RatingStory,
                Description = resolved.Description ?? source.Description
            };
        }
    }
}
